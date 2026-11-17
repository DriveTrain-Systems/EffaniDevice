# Effani hardware

What is known about the hardware the Effani program drives, and where each fact came from. Written 17 November 2026.

Each fact is marked by how sure it is:

- **Confirmed**: read from a manufacturer's page, or stated by the people building the machine.
- **From the program**: what the code, its default settings or a generated board config say. This is what the software
  assumes, not a measurement of the machine, and a machine's saved settings can differ from the defaults.
- **Inferred**: worked out from other facts; not checked against the hardware.
- **Unknown**: not recorded anywhere I have seen.

## Controller electronics

| Item | What is known | Status |
|---|---|---|
| Main controller (board 0) | Duet 3 Mini 5+, WiFi version, running RepRapFirmware 3.x. It has five motor drivers (0 to 4). | From the program; the WiFi version from the setup guide used |
| Second board (board 1) | A board on the CAN bus at address 1. The program treats it as having the same connectors as a Mini 5+ (five drivers, `io` ports, `temp` inputs, `out6`/laser). Its model is not recorded. | From the program; model unknown |
| Firmware | RepRapFirmware 3.x. WPA2-Enterprise WiFi needs 3.5.1 or later with WiFi module firmware 2.1.0 or later. The exact version on the boards is not recorded. | From the program and Duet's documents |
| Connection to the PC | USB serial. The board is found by sending `M115` to each COM port; a Duet identifies itself by USB vendor ID `1D50`. The link is put in raw mode with `M575 P0 S2`. | From the program |
| COM port | COM6 on the development PC when the board was last connected to it (13 and 14 November 2026). The same PC also has a "USB Serial Device (COM3)" that is not the board. | From the app log |
| WiFi | The board's WiFi module, turned on in config.g only when a WiFi SSID is set. | From the program |

Things about the firmware that shaped the program:

- Commands sent over USB are only read when the previous one has finished. An emergency stop (`M112`) sent over USB
  therefore waits behind a running move or homing; it is only acted on at once from the network or the PanelDue port.
  A hardware stop input (`M581 ... T0`) is the only immediate stop.
- `G1` always runs at the feed rate it is given (held to the axis's `M203` maximum); `G0` can run as a rapid move and
  ignore it. The program therefore moves with `G1`.
- With the board in control of its axis limits (`M208`), a move past a limit is cut short at the limit.

## Axes

Seven axes. The values are the program's defaults and the board config generated on 14 November 2026; microstepping is
x16 with interpolation on every axis.

| Axis | Letter | Driver | Endstop input | Endstop end | Travel (mm) | Steps/mm | Current (mA) | Driver mode | Max speed (mm/min) |
|---|---|---|---|---|---|---|---|---|---|
| Table X | X | 1.0 | 1.io0.in | Low | 360 (364 in the 14 Nov config) | 1066.67 | 400 | StealthChop | 900 |
| Table Y | Y | 1.2 | 1.io1.in | High | 555 | 1066.67 | 400 | StealthChop | 900 |
| Aspirate (needle) | Z | 0.0 | io0.in | High | 90 | 1259.84 | 500 | SpreadCycle | 500 |
| DNA Reagent pump | V | 0.4 | io4.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| KOH pump | W | 0.2 | io2.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| NADH pump | A | 0.1 | io1.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| Basal pump | B | 0.3 | io3.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |

Status: from the program.

Other motion defaults: acceleration 500 mm/s² on every axis; move speeds Table X 700 and Table Y 600 mm/min; homing at
600 then 300 mm/min for the table; fluid is dispensed at 5 mm/min of syringe travel.

### Table lead screws (X and Y)

| Item | Value | Status |
|---|---|---|
| Part | McMaster-Carr 7549K75, precision lead screw | Confirmed (named by the builder, 17 Nov 2026) |
| Thread | M12 × 3 mm, metric trapezoidal, DIN 103, class 8e, right hand | Confirmed (McMaster page) |
| Thread starts | 1 | Confirmed |
| Travel per turn (lead) | 3 mm | Confirmed |
| Accuracy of travel | ±0.15 mm per 300 mm | Confirmed |
| Length, material | 1000 mm, 1018 low-carbon steel | Confirmed |

The table's 1066.67 steps/mm is 3200 ÷ 3: a 200-step motor at 16 microsteps turning this screw directly. So the setting
matches the screw.

### Pump and needle screws

The 1259.84 steps/mm of the pumps and the needle is 3200 ÷ 2.54, which implies a screw of 0.1 inch (2.54 mm) per turn
on a 200-step motor at 16 microsteps. Status: inferred from the number; the parts are unknown.

Syringe travel per microlitre, from the program: 0.00153 mm for DNA reagent and basal media, 0.00897 mm for KOH and
NADH, 0.013 mm for the aspiration needle. The syringe sizes are unknown.

### Motors

Unknown: the motor models, their step angle (1.8° or 0.9°), and whether anything (a belt or gears) sits between a motor
and its screw.

## Switched outputs

| Output | Firmware port | Pin (14 Nov config) | Program default | Status |
|---|---|---|---|---|
| Tray UV light | P0 | out4 | board 0, out4 | From the program |
| Chamber UV light | P1 | out5 | board 0, out5 | From the program |
| NADH laser | P2 | 1.out6 (the laser pin of board 1) | board 1, io3.out | From the program |
| DNA laser | P3 | out6 (the laser pin of board 0) | board 1, io2.out | From the program |

They are switched with `M42 P{n} S1` and `S0`, and their state is read with `M409 K"state.gpOut[].pwm"`. On a Mini 5+
the laser pin is the same pin as out6.

The settings fields for the lasers are named "532" for NADH and "488" for DNA, which suggests a 532 nm and a 488 nm
laser. Status: inferred from the names; the laser parts are unknown.

## Sensors

| Sensor | Firmware sensor | Input | Read as | Range | Status |
|---|---|---|---|---|---|
| DNA sensor | S0 | 1.temp0 (a thermistor input of board 1) | linear analog | 0 to 4095 | From the program |
| NADH sensor | S1 | 1.temp1 | linear analog | 0 to 4095 | From the program |

Defined with `M308 S{n} P"{pin}" Y"linear-analog" ... B0 C4095` and read with
`M409 K"sensors.analog[{n}].lastReading"`. A reading is taken 6 seconds after the laser is switched on. The sensor
parts are unknown.

## Sample trays and the table

From the program's defaults; the layout was last revised on 17 November 2026 from positions given by the builder.

- Tray: 8 wells across by 12 up, wells 9 mm apart each way, 6.67 mm wide and 12 mm deep (the layout of a 96-well plate).
- Trays on the carrier: 2 across, 1 up, 87.48 mm from one tray to the next across.
- First well of the first tray: 0,0. The table position that brings a well to an item is the item's location minus the
  well's offset from the first well.
- Locations (table X,Y in mm): tray loading 154,4; tray sterilization 320,20; waste 0,0; DNA sensor 240,385; NADH sensor
  150,386; NADH port 150,184; DNA reagent port 240,257; KOH port 149,261; basal media port 240,186; aspiration needle
  195,109; biopsy loading 194.5,164.
- With two trays, the last column of the second tray is 150.48 mm along X, just past the KOH port (149) and the NADH
  port and sensor (150), so that column cannot be brought to those three.

## Host computer

- A Windows PC with a touchscreen; the program uses the Windows touch keyboard (TabTip).
- The Tools window is sized to 90% of the screen; its layout was checked at 1980 × 1020.
- Program data is in `%LOCALAPPDATA%\Effani`: the settings and assay database `AssayData.effani`, the log `Log.txt`, and
  the generated board files in `sys\`.

## Open questions

1. **Short table moves.** On 17 November 2026, Align Next Well was reported to move the table only about 60% of the way
   to the next well. The program commands exactly one well offset (9 mm) and the screw matches the steps/mm setting, so
   the candidates are: a 0.9° motor (the table would move exactly half), a reduction between motor and screw, missed
   steps at 400 mA in StealthChop, or a tray whose wells are not 9 mm apart. A commanded 100 mm move, measured, tells
   them apart. Not resolved.
2. The model of board 1.
3. The motor models and whether they drive the screws directly.
4. The pump and needle screw parts, and the syringe sizes.
5. The laser, sensor and UV lamp parts.
6. The firmware version on the boards.

## Sources

Manufacturer and firmware documents:

- McMaster-Carr, lead screw 7549K75: https://www.mcmaster.com/product/7549K75 — the table screw's thread and travel per
  turn. Read 17 November 2026.
- Duet3D G-code dictionary: https://docs.duet3d.com/User_manual/Reference/Gcodes — the meaning of every command the
  program sends (`M42`, `M112`, `M308`, `M409`, `M552`, `M574`, `M575`, `M581`, `M587`, `M950` and the rest).
  The same text as a file: https://raw.githubusercontent.com/Duet3D/Duet3D-Documentation/main/en/User_manual/Reference/Gcodes.md
- Duet 3 Mini 5+ hardware overview: https://docs.duet3d.com/Duet3D_hardware/Duet_3_family/Duet_3_Mini_5+_Hardware_Overview
- Connecting a Duet 3 Mini 5+ WiFi: https://docs.duet3d.com/en/User_manual/Connecting_your_Duet/Duet_WiFi_Mini5plus
- RepRapFirmware source, branch 3.5-dev, for how commands from USB are read and when an emergency stop is acted on:
  - https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodes.cpp
  - https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodeInput.cpp
  - https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodeInput.h
- RepRapFirmware 3.x changelog: https://github.com/Duet3D/RepRapFirmware/wiki/Changelog-RRF-3.x
- WiFi module firmware upgrade notes: https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/WiFi%20module%20firmware%20upgrade.md

Duet3D forum threads consulted in earlier work (listed by their titles; I have not re-read them for this file):

- Laser control on a Mini 5+: https://forum.duet3d.com/topic/25062/duet-mini-5-and-co2-50w-laser-guidance
- `G0` speed: https://forum.duet3d.com/topic/29506/g0-speed-not-as-expected
- Mini 5+ WiFi connection: https://forum.duet3d.com/topic/23335/unable-to-connect-duet-3-mini-wifi-to-wifi/3
- Output pin behaviour at power loss: https://forum.duet3d.com/topic/36058/duet3-mini5-fan-pin-behavior-at-power-lost
- Reading an analog sensor: https://forum.duet3d.com/topic/15693/adding-current-sensor-and-readout-to-dwc/13

In this repository and on the development PC:

- `Drivetrain.Robotics.Control/Settings.cs`: the default of every setting, including the board connections.
- `Drivetrain.Robotics.Control/Configuration/DeviceConfiguration.cs`: how config.g and the homing files are generated.
- `Drivetrain.Robotics.Control/Configuration/BoardPort.cs`: how connectors are named.
- `Drivetrain.Robotics.Control/DeviceControl.cs`: how the program talks to the board.
- `%LOCALAPPDATA%\Effani\sys\config.g`: the board config generated on 14 November 2026.
- `%LOCALAPPDATA%\Effani\Log.txt`: the program's log, which shows when a board was connected and on which port.
