# Effani hardware

What is known about the hardware the Effani program drives. Links to the manufacturers' and firmware pages are at the end.

## Controller electronics

| Item | What is known |
|---|---|
| Main controller (board 0) | Duet 3 Mini 5+, WiFi version. It has five motor drivers (0 to 4). |
| Second board (board 1) | A board on the CAN bus at address 1. It is the same board as board 0, a Duet 3 Mini 5+, with the same connectors (five drivers, `io` ports, `temp` inputs, `out6`/laser). |
| Firmware | RepRapFirmware 3.7.0. The version on board 1 is not known. |
| WiFi module firmware | Not known. WPA2-Enterprise WiFi needs RepRapFirmware 3.5.1 or later, which 3.7.0 satisfies, together with WiFi module firmware 2.1.0 or later. |
| Connection to the PC | USB serial. The board is found by sending `M115` to each COM port; a Duet identifies itself by USB vendor ID `1D50`. The link is put in raw mode with `M575 P0 S2`. |
| COM port | COM6 on the development PC when the board was last connected to it. The same PC also has a "USB Serial Device (COM3)" that is not the board. |
| WiFi | The board's WiFi module, turned on in config.g only when a WiFi SSID is set. |

Firmware behaviour the program depends on (not yet checked against 3.7.0):

- Commands sent over USB are only read when the previous one has finished. An emergency stop (`M112`) sent over USB
  therefore waits behind a running move or homing; it is only acted on at once from the network or the PanelDue port.
  A hardware stop input (`M581 ... T0`) is the only immediate stop.
- `G1` always runs at the feed rate it is given (held to the axis's `M203` maximum); `G0` can run as a rapid move and
  ignore it. The program therefore moves with `G1`.
- With the board in control of its axis limits (`M208`), a move past a limit is cut short at the limit.

## Axes

Seven axes. Microstepping is x16 with interpolation on every axis.

| Axis | Letter | Driver | Endstop input | Endstop end | Travel (mm) | Steps/mm | Current (mA) | Driver mode | Max speed (mm/min) |
|---|---|---|---|---|---|---|---|---|---|
| Table X | X | 1.0 | 1.io0.in | Low | 240 | 1600 | 400 | StealthChop | 900 |
| Table Y | Y | 1.2 | 1.io1.in | High | 370 | 1600 | 400 | StealthChop | 900 |
| Aspirate (needle) | Z | 0.0 | io0.in | High | 90 | 1259.84 | 500 | SpreadCycle | 500 |
| DNA Reagent pump | V | 0.4 | io4.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| KOH pump | W | 0.2 | io2.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| NADH pump | A | 0.1 | io1.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |
| Basal pump | B | 0.3 | io3.in | High | 117 | 1259.84 | 1500 | SpreadCycle | 2000 |

Other motion settings: acceleration 500 mm/s² on every axis; move speeds Table X 700 and
Table Y 600 mm/min; homing at 600 then 300 mm/min for the table; fluid is dispensed at 5 mm/min of syringe travel.

The table's figures above are the program's defaults. Settings saved before the table's scale was corrected (see Table
lead screws) still hold the old ones: 1066.67 steps/mm and travel 360 (or 364) and 555.

### Table lead screws (X and Y)

| Item | Value |
|---|---|
| Part | McMaster-Carr 7549K75, precision lead screw |
| Thread | M12 × 3 mm, metric trapezoidal, DIN 103, class 8e, right hand |
| Thread starts | 1 |
| Travel per turn (lead) | 3 mm |
| Accuracy of travel | ±0.15 mm per 300 mm |
| Length, material | 1000 mm, 1018 low-carbon steel |

A 200-step motor at 16 microsteps turning this screw directly would need 1066.67 steps/mm (3200 ÷ 3), which is what
the program was first set to.

**Measured travel.** At 1066.67 steps/mm the table moved exactly two thirds of the distance it was commanded. The table
therefore travels 2 mm for each turn of the motor, not 3, and the setting that makes commanded and real millimetres
agree is 1600 steps/mm (3200 ÷ 2). This disagrees with the 3 mm lead of the 7549K75: either the screw fitted has a 2 mm
lead, or the motor turns three times for every two turns of the screw.

In McMaster-Carr's metric trapezoidal lead screws, 2 mm of travel per turn is the M10 × 2 mm size; its M12 screws are
3 mm per turn. Measuring the outside diameter of the screw fitted (10 mm or 12 mm) therefore tells which of the two
explanations holds. The screw's diameter has not been measured.

The program's defaults now use 1600 steps/mm. Every table position, location and travel length set while the scale
was 1066.67 was in units of two thirds of a millimetre, so the defaults for all of them were multiplied by 2/3: the
same places in real millimetres. The speed, acceleration and speed-change defaults were left as they were (move 700
and 600, maximum 900, homing 600 then 300 mm/min, acceleration 500 mm/s², speed change 120 mm/min): they are now in
real millimetres, so the table moves and accelerates, and its motors turn, half as fast again as they did on the old
scale. Settings saved before
the change keep the old scale until they are changed the same way.

### Table motors (X and Y)

| Item | Value |
|---|---|
| Part | StepperOnline 17HS13-0404S1, NEMA 17 bipolar stepper |
| Step angle | 1.8° (200 full steps per turn) |
| Holding torque | 26 N·cm (36.82 oz·in) |
| Rated current | 0.4 A per phase |
| Phase resistance | 30 Ω |
| Inductance | 37 mH ±20% (at 1 kHz) |
| Size | 42 × 42 mm frame, 34 mm body, 5 mm D-cut shaft, 4 leads |
| Wiring | A+ black, A− green, B+ red, B− blue |

What follows from that:

- **Steps per mm.** 200 steps per turn at 16 microsteps on the measured 2 mm of travel per turn is 1600 steps/mm, the
  default setting (see Table lead screws).
- **Current.** The setting of 400 mA is the motor's rated current. `M906` sets the peak current; a peak equal to the
  rated current gives about 71% of the holding torque while moving, and Duet recommends no more than the rated current
  (up to 1.41 times it only with the standstill current reduced). So there is little room to raise it.
- **Speed.** Duet advises against motors with a rated voltage above 4 V or an inductance above 4 mH. This one is 12 V
  (0.4 A × 30 Ω) and 37 mH. By Duet's formula, revs per second = (2 × supply voltage) ÷ (steps per turn × π ×
  inductance × current), its torque starts to fall at about 2.6 turns per second on a 12 V supply and 5.2 on 24 V. On the
  measured 2 mm of travel per turn that is about 310 mm/min at 12 V and 620 mm/min at 24 V. The formula leaves out the
  motor's resistance and its back emf from turning, both large for this motor, so the real figures are lower. The
  table's move speeds are 700 (X) and 600 (Y) mm/min and its maximum 900, so the table is run beyond the speed where
  this motor loses torque. On the old scale those settings were really 467, 400 and 600 mm/min.

Not known: whether anything (a belt or gears) sits between a motor and its screw, and the supply voltage.

### Fluid pump actuators (DNA reagent, KOH, NADH and basal media)

Each of the four fluid pumps is a linear actuator: a stepper motor, its lead screw and a guide rail slide in one unit,
with a sensor for the zero position.

| Item | Value |
|---|---|
| Part | StepperOnline LMD14S13GF15-120, NEMA 14 stepper lead screw linear actuator |
| Step angle | 1.8° (200 full steps per turn) |
| Screw | Trapezoidal lead screw, 6.35 mm diameter, 179 mm long |
| Travel per turn (lead) | 2.54 mm (0.1 in); 0.0127 mm per full step |
| Stroke | 120 mm at most |
| Speed | 38.1 mm/s at most (2286 mm/min) |
| Thrust, load | 135 N maximum thrust; 300 N rated load |
| Position accuracy | ±0.02 mm |
| Rated current | 1.5 A per phase |
| Phase resistance, inductance | 0.95 Ω ±10%; 1.0 mH ±20% |
| Holding torque | 0.14 N·m (19.83 oz·in) |
| Motor wiring | A+ red, A− red/white, B+ green, B− green/white |
| Zero-position sensor | Photoelectric, NPN output; +5 to 24 V brown, ground blue, signal black |
| Motor size, weight | 35 × 35 × 34 mm; 0.812 kg |

How the program's settings sit against it:

- **Steps per mm.** 200 steps per turn at 16 microsteps on a 2.54 mm lead is 1259.84 steps/mm, which is the setting.
- **Travel.** The setting of 117 mm is inside the 120 mm stroke.
- **Speed.** The maximum and move speeds of 2000 mm/min are under the actuator's 2286 mm/min.
- **Current.** The setting of 1500 mA is the rated current, the most Duet recommends for the peak current `M906` sets.
- **Motor.** At 1.0 mH and 1.4 V (1.5 A × 0.95 Ω) it is well inside Duet's advice of under 4 mH and 4 V, unlike the
  table motors.
- **Endstop.** The pumps home to the high end of their travel on inputs io1 to io4; whether those inputs are these
  built-in sensors is not known.

Syringe travel per microlitre is set to 0.00153 mm for DNA reagent and basal media and 0.00897 mm for KOH and NADH. One
microlitre is one cubic millimetre, so those figures imply syringe bores of about 28.8 mm and 11.9 mm, which are the
sizes of roughly a 50 to 60 ml and a 5 ml syringe. The syringes fitted are not known.

### Aspiration needle actuator

| Item | Value |
|---|---|
| Part | StepperOnline LMD8S11GD05-090, NEMA 8 stepper lead screw linear actuator |
| Other numbers on record for it | LUD2S1160865420, LADES11G005490 |
| Step angle | 1.8° (200 full steps per turn) |
| Screw | Trapezoidal lead screw, 4.76 mm diameter, 124 mm long |
| Travel per turn (lead) | 2.54 mm (0.1 in); 0.0127 mm per full step |
| Stroke | 90 mm at most |
| Speed | 38.1 mm/s at most (2286 mm/min) |
| Rated load | 60 N |
| Position accuracy | ±0.02 mm |
| Rated current | 0.5 A per phase |
| Phase resistance, inductance | 5.0 Ω ±10%; 1.5 mH ±20% |
| Holding torque | 0.01 N·m (1.416 oz·in) |
| Motor wiring | A+ red, A− red/white, B+ green, B− green/white |
| Zero-position sensor | Photoelectric, NPN output; +5 to 24 V brown, ground blue, signal black |
| Motor size, weight | 20 × 20 × 28.2 mm; 0.2 kg |

The program's settings against it: 1259.84 steps/mm is 200 steps at 16 microsteps on a 2.54 mm lead; the travel of
90 mm is the whole stroke; the current of 500 mA is the rated current; the maximum and move speeds of 500 mm/min are
well under the actuator's 2286. The program moves the needle 0.013 mm per microlitre aspirated.

The needle homes at the bottom of its travel (90 mm, the high end), where it is down in the path of the table: the
table crashes into it if it moves while the needle is there. Every homing of the needle must therefore be followed at
once by a move to its starting position (0 by default, fully raised). The program does this after each homing it
asks for, and the needle's generated homing file ends with the same move. The table is homed before the needle; before
the table is homed the needle is raised to its starting position when it is homed and below it. Until the needle has
been homed the board does not know where it is and cannot raise it, and the needle has no sensor at the top of its
travel to raise it against.

## Switched outputs

| Output | Firmware port | Pin in the last generated board config | Default setting |
|---|---|---|---|
| Tray UV light | P0 | out4 | board 0, out4 |
| Chamber UV light | P1 | out5 | board 0, out5 |
| NADH laser | P2 | 1.out6 (the laser pin of board 1) | board 1, io3.out |
| DNA laser | P3 | out6 (the laser pin of board 0) | board 1, io2.out |

They are switched with `M42 P{n} S1` and `S0`, and their state is read with `M409 K"state.gpOut[].pwm"`. On a Mini 5+
the laser pin is the same pin as out6.

### Lasers

| Laser | Part |
|---|---|
| NADH (532 nm) | Lasever LSR532NL-50-FC |
| DNA (488 nm) | Lasever LSR488NL-50-FC |

Lasever Inc. is Ningbo Yuanming Laser Technology Co., Ltd. In the part numbers, "-50" is taken to be 50 mW and "-FC" a
fibre-coupled output.

The LSR532NL series:

| Item | Value |
|---|---|
| Wavelength, mode | 532 nm, TEM00 |
| Operation | Continuous or modulated; modulation 0 to 30 kHz, analog or TTL |
| Temperature control | TEC |
| Warm-up time | Under 1 minute |
| Power stability | Within ±5% over 4 hours |
| Life | 10,000 hours |
| Beam | Under 2 mm diameter, under 1 mrad divergence |
| Laser head | 100 × 40 × 50 mm (a fan adds 15 mm) |
| Power supply | LSR-PS-I, 100 × 58 × 32 mm |

**The 488 nm laser** is taken to follow the LSR532NL figures above in everything but its wavelength. No data sheet for
the LSR488NL has been found.

**How the lasers are switched.** Each Duet laser output drives the TTL modulation input of that laser's supply. The
supply, and so the laser, stays powered and warm; the output only gates the beam, which switches at once (the
modulation input works up to 30 kHz). The warm-up time therefore applies when the machine is powered up, not each time
the program switches a laser on, and the 6 seconds the program waits before a reading is ample.

## Sensors

| Sensor | Firmware sensor | Input | Read as | Range |
|---|---|---|---|---|
| DNA sensor | S0 | 1.temp0 (a thermistor input of board 1) | linear analog | 0 to 4095 |
| NADH sensor | S1 | 1.temp1 | linear analog | 0 to 4095 |

Defined with `M308 S{n} P"{pin}" Y"linear-analog" ... B0 C4095` and read with
`M409 K"sensors.analog[{n}].lastReading"`. A reading is taken 6 seconds after the laser is switched on. The sensor
parts are not known.

On a Duet 3 Mini 5+ only two of the `io` inputs can read an analog voltage: `io3.in` and `io6.in`. The inputs of
io0, io1, io2, io4 and io5 are digital only, so a linear analog sensor defined on one of them is refused. The `temp`
inputs are analog.

Each sensor's input is set by its board number, port kind and port number: the kind is Temp (`tempN`, the default) or
Input (`ioN.in`). An input can have one user only, so a sensor cannot share an `io` input with an endstop or with the
other sensor; the board answers "Pin ... is not free" to the second.

## Sample trays and the table

- Tray: 8 wells across by 12 up, wells 9 mm apart each way, 6.67 mm wide and 12 mm deep (the layout of a 96-well plate).
- Trays on the carrier: 2 across, 1 up, 87.48 mm from one tray to the next across.
- First well of the first tray: 0,0. The table position that brings a well to an item is the item's location minus the
  well's offset from the first well.
- Locations (table X,Y in mm): tray loading 102.67,2.67; tray sterilization 213.33,13.33; waste 0,0; DNA sensor
  160,256.67; NADH sensor 100,257.33; NADH port 100,122.67; DNA reagent port 160,171.33; KOH port 99.33,174; basal media
  port 160,124; aspiration needle 130,72.67; biopsy loading 129.67,102.67.
- To bring every well of two trays to an item, the item's location must be at least 150.48 along X (the last column of
  the second tray) and at least 99 along Y (the top row). The DNA reagent port, the basal media port and the DNA sensor
  (X 160) meet that. The NADH port, the KOH port and the NADH sensor (X about 100), the aspiration needle (X 130) and
  the biopsy location (X 129.67) do not: at X 100 only the first tray and the first two columns of the second can be
  reached. The aspiration needle's Y of 72.67 is also short of 99, so it cannot be brought to the top three rows of a
  tray.

## Host computer

- A Windows PC with a touchscreen; the program uses the Windows touch keyboard (TabTip).
- The Tools window is sized to 90% of the screen; its layout was checked at 1980 × 1020.
- Program data is in `%LOCALAPPDATA%\Effani`: the settings and assay database `AssayData.effani`, the log `Log.txt`, and
  the generated board files in `sys\`.

## Open questions

1. **The table's travel per motor turn.** It is 2 mm, where the 7549K75 screw alone would give 3. Whether a different
   screw is fitted or there is a 3:2 reduction between motor and screw is not known. The program's defaults are
   corrected for it; settings saved earlier are not.
2. The model of board 1.
3. Whether the table motors drive the screws directly, and the supply voltage (VIN).
4. The syringes fitted to the pumps, and whether the pumps' endstop inputs are the actuators' built-in sensors.
5. The data sheets of the lasers as fitted (50 mW, fibre-coupled), the 488 nm one above all. The sensor and UV lamp
   parts.
6. The firmware version of board 1 and of the WiFi module.
7. **Tray reach.** In real millimetres, five of the eight component locations cannot have every well of two trays
   brought to them, and the aspiration needle cannot reach the top three rows of a tray (see Sample trays and the
   table). Either the tray layout settings (two trays, 87.48 mm apart, wells counted away from the first well) or those
   locations do not describe the machine.

## Links

Parts:

- McMaster-Carr lead screw 7549K75: https://www.mcmaster.com/product/7549K75
- McMaster-Carr metric trapezoidal lead screws: https://www.mcmaster.com/products/lead-screws/thread-type~metric-trapezoidal/
- StepperOnline stepper motor 17HS13-0404S1: https://www.omc-stepperonline.com/nema-17-bipolar-1-8deg-26ncm-36-8oz-in-0-4a-12v-42x42x34mm-4-wires-17hs13-0404s1
- StepperOnline linear actuator LMD14S13GF15-120: https://www.omc-stepperonline.com/nema-14-stepper-lead-screw-linear-actuator-1-5a-stroke-120mm-0-14nm-19-83oz-in-lead-2-54mm-0-1-with-sensor-lmd14s13gf15-120
- StepperOnline linear actuator LMD8S11GD05-090: https://www.omc-stepperonline.com/nema-8-stepper-lead-screw-linear-actuator-0-5a-stroke-90mm-0-01nm-1-416oz-in-lead-2-54mm-0-1-with-sensor-lmd8s11gd05-090
- Lasever LSR532NL series: https://www.lasever.com/productinfo/1253441.html
- GoPhotonics, Lasever LSR532NL: https://www.gophotonics.com/products/lasers/lasever-inc-/29-746-lsr532nl

Controller and firmware:

- Duet 3 Mini 5+ hardware overview: https://docs.duet3d.com/Duet3D_hardware/Duet_3_family/Duet_3_Mini_5+_Hardware_Overview
- Connecting a Duet 3 Mini 5+ WiFi: https://docs.duet3d.com/en/User_manual/Connecting_your_Duet/Duet_WiFi_Mini5plus
- Duet3D, Choosing stepper motors: https://docs.duet3d.com/User_manual/Connecting_hardware/Motors_choosing
- Duet3D G-code dictionary: https://docs.duet3d.com/User_manual/Reference/Gcodes
- Duet3D G-code dictionary as a file: https://raw.githubusercontent.com/Duet3D/Duet3D-Documentation/main/en/User_manual/Reference/Gcodes.md
- RepRapFirmware source, GCodes.cpp: https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodes.cpp
- RepRapFirmware source, GCodeInput.cpp: https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodeInput.cpp
- RepRapFirmware source, GCodeInput.h: https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/src/GCodes/GCodeInput.h
- RepRapFirmware 3.x changelog: https://github.com/Duet3D/RepRapFirmware/wiki/Changelog-RRF-3.x
- WiFi module firmware upgrade notes: https://raw.githubusercontent.com/Duet3D/RepRapFirmware/3.5-dev/WiFi%20module%20firmware%20upgrade.md

Duet3D forum threads:

- Laser control on a Mini 5+: https://forum.duet3d.com/topic/25062/duet-mini-5-and-co2-50w-laser-guidance
- `G0` speed: https://forum.duet3d.com/topic/29506/g0-speed-not-as-expected
- Mini 5+ WiFi connection: https://forum.duet3d.com/topic/23335/unable-to-connect-duet-3-mini-wifi-to-wifi/3
- Output pin behaviour at power loss: https://forum.duet3d.com/topic/36058/duet3-mini5-fan-pin-behavior-at-power-lost
- Reading an analog sensor: https://forum.duet3d.com/topic/15693/adding-current-sensor-and-readout-to-dwc/13
