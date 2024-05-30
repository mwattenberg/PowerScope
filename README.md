# SerialPlotPlus
Yet another serial plotting app focusing particularly on the needs of power electronics and control engineers.

# Features
- Designed from the ground up for fast data visualization
  - GPU accelerated visualization based on ScottPlot
  - Binary data format (ASCII also supported)
  - Connect to multiple devicews / data streams simultaneous  
- Hardware agnostic, data can be parsed from any embedded system via
  - UART through USB-to-UART converter (FTDI / Infineon / Cypress)
  - USB CDC
  - BLE
- Virtual channels derived from real-time data steams
- Data analysis
  - Min / Max / Top / Bottom
  - Std. deviation
- Cross platform based on .net 8

![image](https://github.com/mwattenberg/SerialPlotPlus/assets/73757865/bfca3453-1911-4dd6-9af2-43abebac63d1)


# Motivation
There are many tools for plotting serial data publically available such as
- SerialPlot
- Serial Port Plotter
- SerialLab
- Adriuno serial plotter

Additionally, commercial products such as
- Saleae Logic (analog data)
- Electric UI
- MPLAB® Data Visualizer

However, these tools each lack a set of features that SerialPlotPlus tries to combine.



