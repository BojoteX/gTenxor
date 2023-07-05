# gTenxor - SimHub Plugin

## Description

The gTenxor plugin is designed to enhance your simulation experience by controlling a seat belt/harness based on telemetry data. The plugin works by reading the telemetry data provided by the game, then it manipulates the belt tension via servos connected through a serial port. This mechanism simulates the forces experienced by the player in response to in-game acceleration, deceleration, and swaying.

The gTenxor plugin provides an adjustable maximum limit for the tension in the harness, controllable gain parameters, and filter strength for tailoring the simulated forces to the user's preferences. Moreover, it allows for testing, reversing of surge/sway forces, and includes a feature to automatically reset the servos when the game stops running.

## Main Features

1. Seat belt/harness control using telemetry data.
2. Smooth servo operation using a low-pass filter.
3. Adjustable maximum tension limit.
4. Adjustable gain for Surge and Sway.
5. Option to reverse the direction of Surge and Sway.
6. Actions to toggle the belt tension on/off and increment/decrement the max tension setting.
7. Serial connection management to connect to the servo controller.

## Requirements

1. SimHub software.
2. A seat belt/harness with servo controls.
3. Appropriate serial connection setup.
4. gTenxor device (or Arduino UNO)

## Settings

The plugin has several settings available for customization, including offsets for left and right servo, max tension and filter smoothness. 

## Installation

To install the gTenxor plugin, just copy the DLL file in the directory where Simhub is installed. After successful installation, the plugin can be found in the SimHub left menu under "gTenxor Settings".

Please note that you will need to adjust the settings to match your specific setup, including the correct serial device and baud rate. For more detailed installation and setup instructions, please refer to the specific sections below.
