# Changelog

What has changed in Effani, newest first. Every change to the program gets a line here, under **Unreleased**, in the same
commit as the change. When a version is released, the Unreleased entries move under a heading with that version and its date.

Entries are grouped as **Added** (new things), **Changed** (things that work differently), **Fixed** (faults put right) and
**Removed**.

To release a version, say 4.1.2:

1. Put a heading `## 4.1.2 - 2026-11-17` (the version, then the date) under "Unreleased" and above its entries, so that
   Unreleased is left empty.
2. Commit, and tag that commit `v4.1.2`.
3. Push the branch and the tag to GitHub: `git push github develop v4.1.2`. GitHub then drafts a Release called
   "Effani 4.1.2" with that section as its notes (`.github/workflows/release.yml`). If the tagged version has no section
   here, no release is drafted and the run fails saying so.
4. Build `Setup\Effani Installer.aip`, which makes `Effani-Installer-4.1.2.exe`. Then build `Setup\EffaniUpdates.aip`
   with an entry for 4.1.2 whose URL is
   `https://github.com/DriveTrain-Systems/EffaniDevice/releases/download/v4.1.2/Effani-Installer-4.1.2.exe`.
   It writes the update list to `Setup\EffaniUpdates-SetupFiles\EffaniUpdates.txt`.
5. On GitHub, open the draft release, attach `Effani-Installer-4.1.2.exe` and the update list named `EffaniUpdates.txt`,
   and publish it. Installed copies look for updates in the latest published release, so they see the new version from
   that moment.

## Unreleased

### Added

- Board emulator: a new project, Drivetrain.Robotics.Emulator, stands in for the controller board so the program can
  be run and tried with no board connected. Start the program with `--emulator`, or with the environment variable
  `EFFANI_EMULATOR` set to `1` (in Visual Studio, which sets it for you, the launch profile
  "Effani Device (board emulator)"): nothing is then sent to a COM port. The emulated board answers the G-code the
  program sends the way the board does: it runs config.g and the homing files, keeps where each axis is and whether
  it is homed, takes as long over a move as the move would take, refuses to move an axis that is not homed, switches
  outputs, gives sensor readings, receives the files Generate Config sends and restarts. A window shows its axes,
  outputs and sensors and the G-code going to and from it, and can make it run faster than real time. Its files are
  kept in the "Board Emulator" folder beside the program's data, and its record of which config it was sent is kept
  apart from the real board's. It moves nothing, so it shows what the program does, not what the hardware will do.

### Fixed

- The generated homeall.g no longer starts with a check that raises a homed needle before the table homes: the board
  marks every axis as not homed before it runs that file, so the check could never apply. The program itself still
  raises the needle before it homes all axes, and homeX.g and homeY.g keep the check, which does apply there. A G28
  for all axes typed in the command box or sent from a terminal is therefore not protected.

## 4.1.4 - 2026-11-17

### Added

- Tools: a control that issues a command to the device carries a small spinner from the moment the command is queued
  until it is over (it ran, failed, or was cleared from the queue). This covers the buttons, the switches, the axis
  sliders, the Send button (also when Enter sends the command) and the buttons on the Settings tab. A command chosen
  from a Commands... list also marks the Commands... button, which stays in view when the list closes. The spinner
  has no background of its own: the control shows through it.

### Changed

- Program and installer are both version 4.1.4.
- Aspiration needle: whenever the needle is homed it is now moved straight to its starting position, so it is not
  left down at its home position where the table would crash into it. This applies to homing the needle alone, to
  Home All, and to the homing at the start of an assay. The needle's generated homing file (homeZ.g) ends with the
  same move, so a G28 typed in the command box or sent from a terminal also raises it: press Generate Config to send
  the new file to the board.
- Homing the table: the table is still homed before the needle, but the needle is raised to its starting position
  first whenever it is homed and known to be below it, so the table does not run into it. The program does this
  before it homes Table X, Table Y or all axes, and does not home the table if the needle could not be raised; the
  generated homeX.g, homeY.g and homeall.g start with the same check. A needle that has not been homed since the
  board was switched on cannot be raised this way: the board does not know where it is.
- Main screen: the Upload and Tools buttons in the upper right corner have a transparent background.
- Default settings: the table is set to 1600 steps/mm, up from 1066.67. The table was found to move exactly two thirds
  of the distance commanded, because it travels 2 mm per motor turn and not 3. Every default that was set on the old
  scale is multiplied by 2/3 so that it means the same place in real millimetres: the table's travel (X 240, Y 370)
  and the default locations of the tray
  loading and sterilization positions, the sensors, the fluid ports and the aspiration needle. The table's speed, acceleration and
  speed-change defaults are unchanged, so the table now moves at the speeds they state, half as fast again as before. Saved settings keep their own values.
- Updates: the update list no longer has an entry for 4.0.4, whose installer file no longer exists, and has been
  rebuilt with the entry for 4.1.3.
- Settings: the number fields no longer have limits of their own, so they no longer show "Value is out of range"
  or refuse a value while it is being typed. The program checks the settings itself instead: a moment after the last
  change, any setting outside what it allows is listed under the settings pages, with the value it should be at
  least or at most. The Timeout field beside the command box has no limit either, nor does the number field of the
  on-screen input window.
- The Move To and Move... windows and the wizard's Number of animals field have no limits on the field either. Their
  limits (the axis's travel, the room the axis has, 1 to the number of samples the trays hold) are rules checked by
  the same rule check as the settings: the window says what is wrong with the value, and its Move, direction or Next
  button stays off until the value is allowed.
- The wizard's Number of animals field no longer shows "Value '' could not be converted" when it is first displayed
  or emptied: an empty field now counts as 0, which keeps Next off.

## 4.1.3 - 2026-11-17

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
- Repository: `hardware.md` records what is known of the machine's hardware (boards, axes, screws, outputs, sensors,
  trays), how sure each fact is and where it came from. It is listed in the solution under Solution Items.
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
- Settings: every field that takes a decimal number accepts up to five decimal places as it is typed (they took any
  number before). NADH emission volume is a whole number of microlitres, like the other volumes.
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
- Program and installer are both version 4.1.3 (the program was 4.0.4).
- Installer: installed copies look for updates in the latest GitHub Release (its `EffaniUpdates.txt`) and download the
  installer from the release, instead of from Dropbox. The installer file is named `Effani-Installer-4.1.3.exe`, without
  spaces, so that its name is the same once uploaded, and the updates project writes the update list as
  `EffaniUpdates.txt` in `Setup\EffaniUpdates-SetupFiles`, no longer into the Dropbox folder. Copies already installed keep looking in Dropbox until they are updated to a version built with
  this change.
- Repository: the release workflow drafts the GitHub Release instead of publishing it, so the installer and the update
  list can be attached before devices see it.

- Tools, Component Control: the ±1, ±10 and ±100 mm buttons in each axis's Commands... list are replaced by **Move...**,
  which asks how far to move the axis and which way. The distance is typed as a positive number and the direction chosen
  with a button; a direction is only offered when the axis has that much room left on its travel.

### Fixed

- The program kept running after its window was closed. On closing, everything queued is now discarded, the command in
  progress is left to finish without waiting out a long board timeout, and the board's port is closed after it.
