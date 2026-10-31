# Integrate controller commands, labels and feedback

General task: distinguish controller hardware, installed firmware/editor, static text, emitted commands and actual device-state feedback.

1. Identify the exact controller and firmware. Verify its supported command and display features from the manufacturer.
2. Trace one press/release, channel and command value to the Fractal device. Check configured CC assignments, toggle conventions and duplicate routing.
3. Configure a static label only when its assignment is understood. A label naming Delay 1 does not establish that Delay 1 exists in every preset.
4. Verify returned state independently. A controller's LED may reflect its last press while a Fractal scene changes the actual block state.
5. Test preset/scene recall, block presence, bypass, channel state and downstream MIDI reactions separately. Successful outgoing messages do not prove synchronized feedback.

Firmware can change this capability substantially. The historical VOES MX-6 manufacturer comparison distinguished alternate MX-6x6 firmware's static labels/generic MIDI functions from the standard firmware's dedicated Fractal feedback. Confirm the currently installed version and [manufacturer documentation](../catalog/sources.md) before applying that comparison; firmware/editor writes were not verified by this toolkit.

Likewise, a keyboard/note controller is suitable for a note-triggered workflow only when configured to emit the expected notes. Do not infer command capability from its physical size or a product example.

Adding Scene MIDI PC messages, configuring switch labels and synchronizing block LEDs are separate operations. Preserve controller backups where writes are involved and verify the real receiver behavior after any persistent change. See [Scene MIDI editing](scene-midi.md) and [routing diagnostics](midi-reliability.md).
