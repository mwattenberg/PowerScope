# User Guide

[⬅️ Back to README](README.md)

## Overview

PowerScope is a tool for plotting and analyzing data from embedded targets such as MCUs and FPGAs.
The application allows you to visualize real-time data from multiple sources, making debugging and development more efficient.

## Adding Data Streams

PowerScope supports three types of data streams: **Serial**, **Audio**, and **Demo**.
You can add and configure these streams from the main window.

### Serial Data Stream
- Click the **"Add data stream"** button in the left sidebar.
- In the dialog, select **Serial** as the stream type.
- Choose the correct **COM port** and set the **baud rate** (e.g., 3MBaud or higher for high-speed data).
- Configure additional options as needed (parity, stop bits, data format).
- Click **Connect** to start receiving data from your embedded device.

### Audio Data Stream
- Click the **"Add data stream"** button.
- Select **Audio** as the stream type.
- Choose your audio input device (e.g., microphone or line-in).
- Set the sample rate and channel count if needed.
- Click **Connect** to begin streaming audio data.

### Demo Data Stream
- Click the **"Add data stream"** button.
- Select **Demo** as the stream type.
- Configure the number of channels and sample rate for the simulated data.
- Click **Connect** to start a simulated data stream for testing and demonstration.

---

**Tip:**  
You can add multiple streams of any type and view their data simultaneously. Each stream can be configured and managed independently from the sidebar.

