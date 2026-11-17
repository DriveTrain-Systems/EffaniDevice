# Changelog

What has changed in Effani, newest first. Every change to the program gets a line here, under **Unreleased**, in the same
commit as the change. When a version is released, the Unreleased entries move under a heading with that version and its date.

Entries are grouped as **Added** (new things), **Changed** (things that work differently), **Fixed** (faults put right) and
**Removed**.

## Unreleased

### Added

- Tools, Component Control: jog buttons beside each axis's slider that move the axis 5 mm or 1 mm either way.
- Tools, Component Control: **Home** as the first entry of each axis's Commands... list, to home an axis again.
- Settings: an **Undo** button beside Reset. Tools remembers the settings as they were when it opened; Undo puts them back
  and closes Tools, after asking. If the board's config was generated from changed settings in the meantime, it offers to
  generate and send it again from the restored settings.
- Settings: a caution under a component location (fluid ports, aspiration needle, sensors, biopsy location) when not every
  well of the full set of trays could be brought to it, saying which values would work.
- Settings: the **Biopsy Location** can be edited (Component Locations, Tray Carrier Locations).

### Changed

- Tools, Component Control: an axis that is not homed shows only a **Home to enable axis movement** button. Once it is homed
  the row shows its jog buttons, slider and Commands... list, with the list at the right-hand end.
- Tools, Component Control: the axis sliders have a larger handle for use on a touchscreen.
- Tools, Component Control: the "Send Command" lists of the axes and sensors are labelled **Commands...**.
- Tools, Component Control: the command queue and its Clear Command Queue button above the axis list are gone; the ones in
  the Commands area remain.
- Tools: the Commands area can no longer be collapsed, and the tabs are wider by the space its strip took.
- Tools: Clear Command Queue and Clear Messages share one row in the Commands area.
- Tools: the software version, Check for Updates and what the check reported are in the row of the tabs.
- Settings: every spin, text and password field takes effect on each typed character and each spin-button click, not when
  the field is left.
- Settings: the "Fluid Emission" heading is **Fluid Handling**, and Fluid Handling Locations is a sub-heading of Component
  Locations.
- Aligning a well uses the well positions from the settings as they are when the button is pressed, so edits to First Well
  Location, the offsets and the counts take effect without reopening Tools.
- Activating another settings version, saving the settings under a new name or resetting them rebuilds what Tools works
  from (axes, sensors and wells).
- A move waits for the board as long as its distance and speed need, when that is longer than the Move timeout setting.
- config.g turns the board's WiFi off (`M552 S0`) when no WiFi SSID is set, and on when one is.
- Default settings: Tray Loading Location 154,4; DNA sensor 240,385; NADH sensor 150,386; fluid ports NADH 150,184, DNA
  reagent 240,257, KOH 149,261, basal media 240,186; aspiration needle 195,109. The default Biopsy Location is worked out
  from the pump defaults (halfway between the leftmost and rightmost pump, 20 mm below the lowest: 194.5,164). The default
  WiFi SSID and WiFi password are empty. Saved settings keep their own values.
- Installer: product version 4.1.2.

### Fixed

- The program kept running after its window was closed. On closing, everything queued is now discarded, the command in
  progress is left to finish without waiting out a long board timeout, and the board's port is closed after it.
