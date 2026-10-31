using System.Text.Json;
namespace Fractal.Core;

public sealed record ConfiguredCc(int Channel, int Controller, int Value)
{
    public void Validate() { if (Channel is < 1 or > 16 || Controller is < 0 or > 127 || Value is < 0 or > 127) throw new ArgumentException("Explicit CC assignment requires channel 1–16 and controller/value 0–127."); }
}
public sealed record MidiPresetMapping(string Mode, int Channel, int BankController, int Bank, int Program)
{
    public void Validate(int slot)
    {
        new ConfiguredCc(Channel, BankController, Bank).Validate();
        if (Program is < 0 or > 127 || Mode is not ("direct" or "custom")) throw new ArgumentException("Choose explicit direct/custom PC mapping and raw program 0–127.");
        if (Mode == "direct" && (BankController != 0 || Bank != slot / 128 || Program != slot % 128)) throw new ArgumentException("Direct mapping requires CC 0, bank=slot/128, program=slot%128; custom mapping must be explicit.");
    }
}
public sealed record MidiRouteTarget(string Kind, int? Slot = null, int? Scene = null, int? EffectId = null, string? Channel = null,
    bool? Bypassed = null, ConfiguredCc? Cc = null, MidiPresetMapping? PresetMapping = null);
public sealed record MidiRouteRule(string InputKind, int InputChannel, int Number, int? Value, MidiRouteTarget Target);
public sealed record MidiInputEvent(string Kind, int Channel, int Number, int Value);
public sealed record MidiRoutingRequest(int SchemaVersion, string Model, int Capacity, string Firmware, MidiRouteRule[] Rules, MidiInputEvent[] Events);

/// <summary>Finite or host-fed input mapping. Note release and velocity-zero Note On release the held key, never trigger an action.</summary>
public sealed class MidiInputRouter
{
    private readonly MidiRouteRule[] rules;
    private readonly HashSet<(int Channel, int Note)> held = [];
    private readonly Dictionary<(int Channel, int Controller), int> cc = [];
    public MidiInputRouter(DeviceDefinition device, string firmware, MidiRouteRule[] inputs)
    {
        _ = EditReadCatalogue.Capabilities(device, firmware);
        if (inputs == null || inputs.Length is < 1 or > 64 || inputs.Any(r => r == null)) throw new ArgumentException("Choose 1–64 explicit routing rules.");
        rules = JsonSerializer.Deserialize<MidiRouteRule[]>(JsonSerializer.Serialize(inputs, LiveControlScope.JsonOptions), LiveControlScope.JsonOptions)!;
        foreach (var r in rules)
        {
            if (r.InputKind is not ("note" or "cc") || r.InputChannel is < 1 or > 16 || r.Number is < 0 or > 127 ||
                r.InputKind == "note" && r.Value != null || r.InputKind == "cc" && r.Value is null or < 0 or > 127) throw new ArgumentException("Invalid note/CC input rule.");
            var t = r.Target ?? throw new ArgumentException("Missing target.");
            if (t.Kind == "preset")
            {
                if (t.Slot == null || t.Scene != null || t.EffectId != null || t.Channel != null || t.Bypassed != null || t.Cc != null || t.PresetMapping == null) throw new ArgumentException("Preset target requires slot and explicit bank/PC mapping only.");
                device.ValidateSlot(t.Slot.Value); t.PresetMapping.Validate(t.Slot.Value);
            }
            else
            {
                if (t.Slot != null || t.PresetMapping != null || t.Cc == null) throw new ArgumentException("Live target requires an explicit CC assignment.");
                t.Cc.Validate();
                bool valid = t.Kind switch
                {
                    "scene" => t.Scene is >= 1 and <= 8 && t.EffectId == null && t.Channel == null && t.Bypassed == null,
                    "channel" => t.Scene == null && t.EffectId is >= 1 and <= 1000 && t.Channel is "A" or "B" or "C" or "D" && t.Bypassed == null,
                    "bypass" => t.Scene == null && t.EffectId is >= 1 and <= 1000 && t.Channel == null && t.Bypassed != null,
                    _ => false
                };
                if (!valid) throw new ArgumentException("Route targets must describe one absolute preset/scene/channel/bypass intent, never a toggle.");
            }
        }
        if (rules.Select(r => (r.InputKind, r.InputChannel, r.Number, r.Value)).Distinct().Count() != rules.Length) throw new ArgumentException("Duplicate input binding; resolve the route instead of sending twice.");
    }
    public MidiRouteTarget? Route(MidiInputEvent input)
    {
        if (input == null || input.Kind is not ("note-on" or "note-off" or "cc") || input.Channel is < 1 or > 16 || input.Number is < 0 or > 127 || input.Value is < 0 or > 127) throw new ArgumentException("Invalid MIDI input event.");
        var key = (input.Channel, input.Number); string kind;
        if (input.Kind == "note-off" || input.Kind == "note-on" && input.Value == 0) { held.Remove(key); return null; }
        if (input.Kind == "note-on") { if (!held.Add(key)) return null; kind = "note"; }
        else { if (cc.TryGetValue(key, out int prior) && prior == input.Value) return null; cc[key] = input.Value; kind = "cc"; }
        return rules.SingleOrDefault(r => r.InputKind == kind && r.InputChannel == input.Channel && r.Number == input.Number && (kind == "note" || r.Value == input.Value))?.Target;
    }
    public static object Plan(byte[] json)
    {
        if (json.Length > 1_000_000) throw new InvalidDataException("Routing request exceeds bound.");
        var request = JsonSerializer.Deserialize<MidiRoutingRequest>(json, LiveControlScope.JsonOptions) ?? throw new InvalidDataException("Missing routing request.");
        if (request.SchemaVersion != 1 || request.Events == null || request.Events.Length is < 1 or > 256) throw new ArgumentException("Choose 1–256 observed input events.");
        var router = new MidiInputRouter(DeviceDefinition.Create(request.Model, request.Capacity), request.Firmware, request.Rules);
        return new { model = request.Model, firmware = request.Firmware, events = request.Events.Select((e, i) => new { index = i, input = e, target = router.Route(e) }).ToArray(),
            ports_opened = false, automatic_execution = false, note = "Mapped targets feed capture-bound live-control or preset-recall reviews; assignments must match actual device configuration." };
    }
}
