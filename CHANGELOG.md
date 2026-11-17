# Changelog

What has changed in Effani, newest first. Every change to the program gets a line here, under **Unreleased**, in the same
commit as the change. When a version is released, the Unreleased entries move under a heading with that version and its date.

Entries are grouped as **Added** (new things), **Changed** (things that work differently), **Fixed** (faults put right) and
**Removed**.

To release a version, say 4.1.2:

1. Put a heading `## 4.1.2 - 2026-11-17` (the version, then the date) under "Unreleased" and above its entries, so that
   Unreleased is left empty.
2. Commit, and tag that commit `v4.1.2`.
3. Push the branch and the tag to GitHub: `git push github develop v4.1.2`.

GitHub then publishes a Release called "Effani 4.1.2" with that section as its notes (`.github/workflows/release.yml`). If
the tagged version has no section here, no release is made and the run fails saying so.

## Unreleased

### Added

- Tools, Component Control: jog buttons beside each axis's slider that move the axis 5 mm or 1 mm either way.
- Tools, Component Control: **Home** as the first entry of each axis's Commands... list, to home an axis again.
- Tools, Component Control: a gear button on each axis opens, under the axis, the settings that belong to it (its motor,
  its driver and endstop connections and, for a pump or the needle, its volumes, syringe travel and location), so they can
  be changed without going to the Settings tab. They are the same settings as on the Settings tab and each shows changes
  made in the other; Generate Config is offered there when a change needs it.
- Debug builds only: a button beside the on-screen keyboard button in Tools shows every axis as homed (or goes back to
  what the board reports), so the controls of a homed axis can be tried without a board.
- Settings: an **Undo** button beside Reset. Tools remembers the settings as they were when it opened; Undo puts them back
  and closes Tools, after asking. If the board's config was generated from changed settings in the meantime, it offers to
  generate and send it again from the restored settings.
- Settings: a caution under a component location (fluid ports, aspiration needle, sensors, biopsy location) when not every
  well of the full set of trays could be brought to it, saying which values would work.
- Settings: the **Biopsy Location** can be edited (Component Locations, Tray Carrier Locations).
- Repository: pushing a version tag to GitHub publishes a GitHub Release whose notes are that version's section of this file.
- Settings record the version of the program they were created with or last brought up to date by (not shown anywhere).
  When the program starts, saved settings from an older version are brought up to date by the migrations defined for the
  versions in between, then marked with the running version. No migrations are defined yet. A debug build asks first,
  saying what it would change, and leaves the saved settings alone if the answer is no.

### Changed

- Tools, Component Control: an axis that is not homed shows only a **Home to enable axis movement** button. Once it is homed
  the row shows its jog buttons, slider and Commands... list, with the list at the right-hand end.
- Tools, Component Control: the axis sliders have a larger handle for use on a touchscreen.
- Tools, Component Control: every axis's slider and its box are the same length and line up, whatever the axis's travel.
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

- Tools, Component Control: the ±1, ±10 and ±100 mm buttons in each axis's Commands... list are replaced by **Move...**,
  which asks how far to move the axis and which way. The distance is typed as a positive number and the direction chosen
  with a button; a direction is only offered when the axis has that much room left on its travel.

### Fixed

- The program kept running after its window was closed. On closing, everything queued is now discarded, the command in
  progress is left to finish without waiting out a long board timeout, and the board's port is closed after it.
