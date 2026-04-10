# PowerScope Slave Firmware Specification

## Executive Summary

This document defines the firmware requirements for an **Infineon PSoC** microcontroller acting as an SPI **slave device** to PowerScope's FTDI master. The slave buffers sensor data and transmits it over SPI using a **piggybacked next-count protocol** that minimizes costly USB round-trips.

**Key Characteristics:**
- **MCU Platform**: Infineon PSoC
- **FTDI Chip**: FT2232H (dual-channel, 4K RX buffer per channel)
- **Protocol**: SPI Slave, 4-wire only (SCLK, MOSI, MISO, SS) through SPI isolator
- **SPI Mode**: MODE 0 (CPOL=0, CPHA=0)
- **Clock Speed**: Up to 30 MHz (master-driven)
- **Data Format**: Binary with frame markers (little-endian)
- **Frame Structure**: 2-byte marker + N-byte channel samples
- **Buffer Size**: 4096 bytes minimum (circular ring buffer on PSoC)

**Hardware Constraints:**
- SPI isolator limits interface to exactly 4 wires (SCLK, MOSI, MISO, SS)
- No DATA_READY GPIO is possible — this is a hard limit, not a future optimization
- FT2232H has a 4096-byte RX buffer per channel — transfers are clamped to fit
- Each USB round-trip to the FTDI costs 200µs best case, up to 5ms worst case
- The protocol is designed to minimize the number of USB round-trips

---

## 1. Protocol Design

### 1.1 The Problem with Query-Based Polling

Each `_spi.ReadWrite()` call from the master triggers a USB round-trip:

| Operation | USB Round-Trips | Wall-Clock Cost |
|-----------|----------------|-----------------|
| Query command (4 bytes) | 1 | 200µs - 5ms |
| Count response (2 bytes) | 1 | 200µs - 5ms |
| Data transfer (N bytes) | 1 | 200µs - 5ms |
| **Total per cycle** | **3** | **0.6ms - 15ms** |

At worst case, 3 round-trips × 5ms = 15ms per cycle. This severely limits throughput.

### 1.2 Piggybacked Next-Count Protocol

**Key idea**: The slave appends a 2-byte "next transfer size" at the end of every data transfer. The master uses this to know exactly how many bytes to request next time, eliminating the query and count phases entirely.

**Steady-state operation: 1 USB round-trip per cycle.**

```
MASTER (FTDI)                      SLAVE (PSoC)
    |                                  |
    |  Initial Query (first cycle only)|
    |  TX: [0xFF 0x00 0xFF 0xFF]       |
    |  TX: [0x00 0x00] (count request) |
    |--------------------------------->|
    |  RX: [count_hi, count_lo]        | Report available bytes
    |                                  |
    |  Data Request (N + 2 bytes)      |
    |  TX: [0x00 × (N+2)]             |
    |--------------------------------->|
    |  RX: [N data bytes]             |  Send buffered frames
    |  RX: [next_hi, next_lo]         |  Piggyback: bytes ready for next time
    |                                  |
    |  (master sleeps ~5ms)            |
    |                                  | Continue sampling, buffer fills
    |                                  |
    |  Next Data Request (M + 2 bytes) |
    |  TX: [0x00 × (M+2)]             | M = next count from previous transfer
    |--------------------------------->|
    |  RX: [M data bytes]             |
    |  RX: [next_hi, next_lo]         |  New piggyback count
    |                                  |
    |         (repeat)                 |
```

### 1.3 Timing Comparison

| Protocol | Round-Trips/Cycle | Best Case | Worst Case |
|----------|-------------------|-----------|------------|
| Query-based (old) | 3 | 0.6ms | 15ms |
| **Piggybacked (new)** | **1** | **0.2ms** | **5ms** |

### 1.4 Initial Bootstrap

The very first cycle has no previous "next count", so the master performs a one-time query:
1. Send query command (4 bytes) — 1 round-trip
2. Read count response (2 bytes) — 1 round-trip
3. Read data + piggybacked next count — 1 round-trip

After bootstrap, every subsequent cycle is a single round-trip.

### 1.5 Edge Cases

| Situation | Slave Behavior | Master Behavior |
|-----------|---------------|-----------------|
| No data available at boot | Count response = 0 | Re-send query after sleep |
| Slave promised N but has less | Pad with 0x00, next count = 0 | Parser ignores padding (frame markers protect) |
| Slave has more than promised | Extra data stays in ring buffer | Picked up on next cycle |
| Slave promised N but has more | Send N bytes + actual next count | Works normally |

---

## 2. SPI Hardware Configuration

### 2.1 Pin Connections (Through SPI Isolator)

| FT232H Pin | Signal | Direction | Isolator | PSoC Pin |
|------------|--------|-----------|----------|----------|
| D0 | SCLK | Master ? Slave | Isolated | SPI SCLK |
| D1 | MOSI | Master ? Slave | Isolated | SPI MOSI |
| D2 | MISO | Slave ? Master | Isolated | SPI MISO |
| D3 | SS | Master ? Slave | Isolated | SPI SS |

**Hard limit**: The SPI isolator provides exactly 4 lines. No additional signals are possible.

### 2.2 SPI Mode

**SPI Mode 0** (CPOL=0, CPHA=0):
- Clock idles LOW
- Data captured on rising edge of SCLK
- Data changed on falling edge of SCLK
- Must match `_spiMode = 0` in `FTDI_SerialDataStream` constructor

---

## 3. Slave Firmware Architecture

### 3.1 Component Overview

```
+------------------------------------------------------+
|  Timer ISR (1ms)                                     |
|  - Sample ADC channels                               |
|  - Format frame: [0xAA 0x55] + channel data          |
|  - Write frame to Ring Buffer                        |
+------------------------------------------------------+
                           | write frames
                           v
+------------------------------------------------------+
|  Ring Buffer (4096 bytes, circular)                   |
|  - write_ptr: updated by Timer ISR                   |
|  - read_ptr: updated by SPI TX logic                 |
|  - available = write_ptr - read_ptr                  |
+------------------------------------------------------+
                           | read bytes for TX
                           v
+------------------------------------------------------+
|  SPI Protocol Handler (ISR-driven)                   |
|  - Phases: QUERY -> COUNT -> DATA+NEXTCOUNT          |
|  - Appends 2-byte next-count after data              |
|  - RX FIFO ISR: track incoming bytes                 |
|  - TX FIFO ISR: refill TX from ring buffer           |
+------------------------------------------------------+
```

### 3.2 State Machine

```
    +-------------+
    |    IDLE     |<----------------------------+
    +------+------+                             |
           | SPI byte received                  |
           | Match query pattern                |
           v                                    |
    +-----------------+                         |
    | RECEIVING_CMD   |                         |
    | Match 4 bytes:  |  Mismatch -> IDLE       |
    | FF 00 FF FF     |-----+                   |
    +---------+-------+     |                   |
              | All matched |                   |
              v             |                   |
    +-----------------+     |                   |
    | SENDING_COUNT   |     |                   |
    | TX: hi, lo      |     |                   |
    | (2 bytes)       |     |                   |
    +---------+-------+     |                   |
              | Done        |                   |
              v             |                   |
    +-----------------+     |                   |
    | SENDING_DATA    |     |                   |
    | TX from ring    |     |                   |
    | (N bytes)       |     |                   |
    +---------+-------+     |                   |
              | N sent      |                   |
              v             |                   |
    +-------------------+   |                   |
    | SENDING_NEXTCOUNT |   |                   |
    | TX: next_hi,      |   |                   |
    |     next_lo       |   |                   |
    +---------+---------+   |                   |
              | 2 bytes sent|                   |
              +-------------+-------------------+
```

---

## 4. Detailed Implementation

### 4.1 Protocol Constants

```c
// Query command (master -> slave, first cycle only)
#define QUERY_CMD_BYTE0  0xFF
#define QUERY_CMD_BYTE1  0x00
#define QUERY_CMD_BYTE2  0xFF
#define QUERY_CMD_BYTE3  0xFF
#define QUERY_CMD_LENGTH 4

// Sizes
#define COUNT_RESPONSE_LENGTH   2   // Byte count: big-endian uint16
#define NEXT_COUNT_LENGTH       2   // Piggybacked next count: big-endian uint16
```

### 4.2 Protocol State Machine

```c
typedef enum {
    STATE_IDLE,
    STATE_CMD_BYTE1,
    STATE_CMD_BYTE2,
    STATE_CMD_BYTE3,
    STATE_SENDING_COUNT,
    STATE_SENDING_DATA,
    STATE_SENDING_NEXT_COUNT
} SpiProtocolState;

volatile SpiProtocolState spi_state = STATE_IDLE;
volatile uint16_t spi_tx_index = 0;
volatile uint16_t spi_data_bytes_to_send = 0;
uint8_t count_response[2];
uint8_t next_count_response[2];

void OnSPI_ByteReceived(uint8_t rx_byte)
{
    switch (spi_state)
    {
        case STATE_IDLE:
            if (rx_byte == QUERY_CMD_BYTE0)
                spi_state = STATE_CMD_BYTE1;
            break;

        case STATE_CMD_BYTE1:
            if (rx_byte == QUERY_CMD_BYTE1)
                spi_state = STATE_CMD_BYTE2;
            else
                spi_state = STATE_IDLE;
            break;

        case STATE_CMD_BYTE2:
            if (rx_byte == QUERY_CMD_BYTE2)
                spi_state = STATE_CMD_BYTE3;
            else
                spi_state = STATE_IDLE;
            break;

        case STATE_CMD_BYTE3:
            if (rx_byte == QUERY_CMD_BYTE3)
            {
                // Query recognized. Prepare count response.
                uint16_t available = ring_buffer_available();

                count_response[0] = (uint8_t)((available >> 8) & 0xFF);
                count_response[1] = (uint8_t)(available & 0xFF);
                spi_tx_index = 0;

                SPI_WriteTxFifo(count_response[0]);
                spi_state = STATE_SENDING_COUNT;
            }
            else
            {
                spi_state = STATE_IDLE;
            }
            break;

        case STATE_SENDING_COUNT:
            spi_tx_index++;
            if (spi_tx_index < COUNT_RESPONSE_LENGTH)
            {
                SPI_WriteTxFifo(count_response[spi_tx_index]);
            }
            else
            {
                // Count sent. Prepare data phase.
                spi_data_bytes_to_send = (count_response[0] << 8) | count_response[1];
                spi_tx_index = 0;
                spi_state = STATE_SENDING_DATA;

                if (spi_data_bytes_to_send > 0)
                    SPI_WriteTxFifo(ring_buffer_read_byte());
                else
                    spi_state = STATE_SENDING_NEXT_COUNT;
            }
            break;

        case STATE_SENDING_DATA:
            spi_tx_index++;
            if (spi_tx_index < spi_data_bytes_to_send)
            {
                SPI_WriteTxFifo(ring_buffer_read_byte());
            }
            else
            {
                // All data sent. Now send piggybacked next count.
                uint16_t next_available = ring_buffer_available();

                next_count_response[0] = (uint8_t)((next_available >> 8) & 0xFF);
                next_count_response[1] = (uint8_t)(next_available & 0xFF);
                spi_tx_index = 0;

                SPI_WriteTxFifo(next_count_response[0]);
                spi_state = STATE_SENDING_NEXT_COUNT;
            }
            break;

        case STATE_SENDING_NEXT_COUNT:
            spi_tx_index++;
            if (spi_tx_index < NEXT_COUNT_LENGTH)
            {
                SPI_WriteTxFifo(next_count_response[spi_tx_index]);
            }
            else
            {
                // Full transfer complete (data + next count)
                SPI_WriteTxFifo(0x00);
                spi_state = STATE_IDLE;
            }
            break;
    }
}

void OnSS_Rising_Edge(void)
{
    spi_state = STATE_IDLE;
    spi_tx_index = 0;
}
```

### 4.3 Ring Buffer

```c
#define RING_BUFFER_SIZE 4096

typedef struct {
    uint8_t data[RING_BUFFER_SIZE];
    volatile uint16_t write_ptr;
    volatile uint16_t read_ptr;
} RingBuffer;

RingBuffer g_ring_buffer = { .write_ptr = 0, .read_ptr = 0 };

uint16_t ring_buffer_available(void)
{
    uint16_t w = g_ring_buffer.write_ptr;
    uint16_t r = g_ring_buffer.read_ptr;

    if (w >= r)
        return w - r;
    else
        return RING_BUFFER_SIZE - r + w;
}

void ring_buffer_write(uint8_t* data, uint16_t len)
{
    for (uint16_t i = 0; i < len; i++)
    {
        g_ring_buffer.data[g_ring_buffer.write_ptr] = data[i];
        g_ring_buffer.write_ptr = (g_ring_buffer.write_ptr + 1) % RING_BUFFER_SIZE;
    }
}

uint8_t ring_buffer_read_byte(void)
{
    uint8_t val = g_ring_buffer.data[g_ring_buffer.read_ptr];
    g_ring_buffer.read_ptr = (g_ring_buffer.read_ptr + 1) % RING_BUFFER_SIZE;
    return val;
}
```

### 4.4 Frame Generation (Timer ISR)

```c
#define FRAME_MARKER_HI 0xAA
#define FRAME_MARKER_LO 0x55
#define NUM_CHANNELS 3
#define FRAME_SIZE (2 + NUM_CHANNELS * 2)  // 2 marker + 6 data = 8 bytes

void OnTimer_1ms_ISR(void)
{
    int16_t channels[NUM_CHANNELS];

    channels[0] = ADC_Read(0);
    channels[1] = ADC_Read(1);
    channels[2] = ADC_Read(2);

    uint8_t frame[FRAME_SIZE];
    frame[0] = FRAME_MARKER_HI;
    frame[1] = FRAME_MARKER_LO;

    for (int ch = 0; ch < NUM_CHANNELS; ch++)
    {
        frame[2 + ch * 2 + 0] = (uint8_t)((channels[ch] >> 0) & 0xFF);  // Low byte
        frame[2 + ch * 2 + 1] = (uint8_t)((channels[ch] >> 8) & 0xFF);  // High byte
    }

    ring_buffer_write(frame, FRAME_SIZE);
}
```

### 4.5 PSoC SPI FIFO Threshold for TX Refill

During `STATE_SENDING_DATA`, use the TX FIFO threshold ISR to bulk-refill:

```c
void OnSPI_TxFifo_BelowThreshold_ISR(void)
{
    if (spi_state == STATE_SENDING_DATA)
    {
        while (SPI_TxFifoNotFull() && spi_tx_index < spi_data_bytes_to_send)
        {
            SPI_WriteTxFifo(ring_buffer_read_byte());
            spi_tx_index++;
        }

        // If all data bytes loaded, prepare next count
        if (spi_tx_index >= spi_data_bytes_to_send)
        {
            uint16_t next_available = ring_buffer_available();
            next_count_response[0] = (uint8_t)((next_available >> 8) & 0xFF);
            next_count_response[1] = (uint8_t)(next_available & 0xFF);
            spi_tx_index = 0;

            SPI_WriteTxFifo(next_count_response[0]);
            spi_state = STATE_SENDING_NEXT_COUNT;
        }
    }
}
```

---

## 5. Data Format

### 5.1 Frame Structure

**8 bytes per frame (example: 3 channels, int16_t)**

```
Offset  Size    Content                  Byte Order
------  ------  -----------------------  -----------
0       2       Frame Marker (0xAA 0x55) Fixed
2       2       Channel 0 (int16_t)      Little-endian
4       2       Channel 1 (int16_t)      Little-endian
6       2       Channel 2 (int16_t)      Little-endian
```

### 5.2 Transfer Structure (Piggybacked)

Each SPI transfer from slave looks like:

```
[frame0][frame1]...[frameN][next_count_hi][next_count_lo]
|<---- data bytes ------->|<--- 2 bytes piggyback ------>|
|<---- count from previous transfer ---->|
```

The master requests `previous_next_count + 2` bytes total. It strips the last 2 bytes as the next count and passes the rest to the parser.

### 5.3 Byte Order Summary

| Field | Byte Order | Example |
|-------|------------|---------|
| Frame marker | Fixed: 0xAA 0x55 | - |
| Channel data (int16_t) | Little-endian | 1000 = 0xE8 0x03 |
| Count response | Big-endian | 320 = 0x01 0x40 |
| Piggybacked next count | Big-endian | 320 = 0x01 0x40 |

### 5.4 Master Parser Configuration

```csharp
byte[] frameStart = new byte[] { 0xAA, 0x55 };
DataParser parser = new DataParser(
    DataParser.BinaryFormat.int16_t,
    numberOfChannels: 3,
    frameStart: frameStart
);

FTDI_SerialDataStream stream = new FTDI_SerialDataStream(
    deviceIndex: 0,
    clockFrequency: 30000000,  // 30 MHz (FT2232H max)
    channelCount: 3,
    parser: parser,
    spiMode: 0
);
```

---

## 6. Master-Side Protocol (FTDI_SerialDataStream.cs)

The master read loop changes from 3 round-trips to 1:

```csharp
// Bootstrap: query + count (only on first cycle)
int nextByteCount = QuerySlaveByteCount();

while (streaming)
{
    if (nextByteCount <= 0)
    {
        // No data promised. Re-query (fallback, costs 2 round-trips).
        nextByteCount = QuerySlaveByteCount();
        if (nextByteCount <= 0)
        {
            Thread.Sleep(1);
            continue;
        }
    }

    // Single round-trip: request data + 2 bytes piggybacked next count
    int totalBytes = nextByteCount + NEXT_COUNT_LENGTH;
    byte[] response = _spi.ReadWrite(new byte[totalBytes]);

    // Split response: data frames | next count
    byte[] frameData = response[0..nextByteCount];
    int nextCount = (response[nextByteCount] << 8) | response[nextByteCount + 1];

    // Process frame data through parser
    ProcessReceivedData(frameData);

    // Use piggybacked count for next cycle
    nextByteCount = nextCount;

    Thread.Sleep(1);
}
```

---

## 7. PSoC-Specific Notes

### 7.1 SPI Component Setup

- **SCB** in SPI Slave mode
- 8-bit, MSB first, Mode 0, hardware SS
- Enable RX FIFO interrupt (byte received)
- Enable TX FIFO interrupt (below threshold for refill)

### 7.2 Interrupt Priorities

| Interrupt | Priority | Rationale |
|-----------|----------|-----------|
| SPI RX FIFO | Highest | Must not miss bytes |
| SPI TX FIFO Threshold | High | Refill TX before underrun |
| Timer (1ms) | Medium | Frame generation tolerates jitter |
| ADC Complete | Low | Can be polled |

### 7.3 Recommended SPI Clock

Start with **1 MHz** for initial development. At 30 MHz one byte arrives every 267ns which is too fast for per-byte ISR handling — use DMA or FIFO bulk transfers at high speeds. Increase clock speed incrementally after the protocol is verified.

The FT2232H MPSSE master clock is 60 MHz. SPI clock = 60 / ((1 + DIVISOR) × 2):
- Divisor 0 = 30 MHz (maximum)
- Divisor 1 = 15 MHz
- Divisor 14 = 2 MHz
- Divisor 29 = 1 MHz

---

## 8. Implementation Checklist

### Phase 1: SPI Communication (No Data)
- [ ] Configure PSoC SPI as slave, Mode 0
- [ ] Wire through SPI isolator to FT232H
- [ ] Implement query recognition (match 0xFF 0x00 0xFF 0xFF)
- [ ] Respond with hardcoded count (e.g., 0x00 0x08 = 8 bytes)
- [ ] Verify master receives correct count in debug output

### Phase 2: Static Data + Piggybacked Count
- [ ] Implement ring buffer
- [ ] Fill with known constant frames
- [ ] Append 2-byte next count after data
- [ ] Verify master strips next count and parses frames correctly
- [ ] Verify steady-state runs at 1 round-trip per cycle

### Phase 3: Live Sensor Data
- [ ] Implement 1ms timer ISR with ADC sampling
- [ ] Generate frames with real sensor data
- [ ] Verify PowerScope shows live waveforms
- [ ] Verify sample rate matches expectations

### Phase 4: Speed Optimization
- [ ] Increase SPI clock (1MHz -> 4MHz -> 15MHz)
- [ ] Enable TX FIFO threshold refill
- [ ] Measure actual throughput
- [ ] Tune ring buffer size

---

## 9. Troubleshooting

### Master receives next count = 0 repeatedly
- Slave not appending next count -> verify STATE_SENDING_NEXT_COUNT
- Ring buffer empty -> verify timer ISR is running
- State machine stuck -> verify SS rising edge resets to IDLE

### Data corruption
- Frame markers (0xAA 0x55) protect against misalignment
- Verify little-endian byte order in frame generation
- Reduce SPI clock speed to rule out timing issues

### Slave promised N bytes but master gets garbage
- Slave had less data than promised -> it must pad with 0x00
- Ring buffer read/write pointer race -> use volatile, verify ISR safety

### Master falls back to query too often
- nextByteCount = 0 means slave had nothing at snapshot time
- Normal if sensor sampling is slower than query rate
- Consider increasing timer frequency or buffer more frames

---

## 10. Performance Analysis

### 10.1 Bottleneck Chain

The system has four potential bottlenecks. The tightest one determines throughput:

```
PSoC ADC (25+ Msps)  -->  SPI Bus (30 MHz)  -->  FTDI RX Buffer (4K)  -->  USB Round-Trip
      not a limit           wire limit            chunk limit              THE bottleneck
```

The PSoC can always produce data faster than the interface can consume it. The PSoC ring buffer absorbs the excess, and the slave reports only what it has. The question is: **how fast can the master drain data?**

### 10.2 The FT2232H 4K RX Buffer Constraint

The FT2232H has a **4096-byte RX buffer per channel**. This is the on-chip FIFO that holds received SPI data before the FTDI sends it to the PC over USB.

**What happens when a transfer exceeds 4K:**
1. FTDI starts clocking SPI — slave data fills the 4K RX buffer
2. After 4096 bytes, the RX buffer is full
3. FTDI must flush the buffer to the PC over USB before it can continue SPI clocking
4. SPI clock **stalls** mid-transfer — the slave sees a pause
5. USB flush completes, SPI clocking resumes
6. The slave continues from where it left off (state machine is fine)

This is not a data corruption problem — it works. But it adds an extra USB round-trip per 4K chunk, which defeats the single-round-trip optimization.

**Solution**: The master clamps each transfer to **4094 data bytes + 2 next-count bytes = 4096 total**. This guarantees the entire transfer fits in one RX buffer flush. If the slave has more data than 4094 bytes, the excess stays in the PSoC ring buffer and is reported via the piggybacked next-count.

```csharp
// In FTDI_SerialDataStream.cs
private const int FTDI_RX_BUFFER_SIZE = 4096;
private const int MAX_TRANSFER_DATA_BYTES = FTDI_RX_BUFFER_SIZE - NEXT_COUNT_LENGTH; // 4094
```

### 10.3 What Happens Inside One Cycle

Each `_spi.ReadWrite()` call in FtdiSharp originally executed 3 USB operations:

```
FtdiDevice.FlushBuffer()     USB round-trip: query RX queue + drain     ~0.5-2ms
FtdiDevice.Write(command)    USB write: send MPSSE command + data       ~0.5-2ms
FtdiDevice.Read(response)    USB read: wait for SPI + receive data      ~0.5-2ms
                                                              Total:    ~1.5-6ms
```

**Optimization applied**: The steady-state streaming loop now uses `ReadWriteNoFlush()` which skips the `FlushBuffer()` call entirely. The piggybacked protocol guarantees we always read exactly what the slave produces, so no stale data accumulates. This reduces each cycle from 3 USB operations to 2:

```
FtdiDevice.Write(command)    USB write: send MPSSE command + data       ~0.5-2ms
FtdiDevice.Read(response)    USB read: wait for SPI + receive data      ~0.5-2ms
                                                              Total:    ~1-4ms
```

The bootstrap `QuerySlaveByteCount()` still uses the original `ReadWrite()` with flush, since stale data may exist at startup.

### 10.4 Master-Side Overhead (Eliminated)

Three sources of wasted time have been removed from the read loop:

1. **`Thread.Sleep(1)` removed** — On Windows, `Thread.Sleep(1)` actually sleeps 1-15ms due to the default system timer resolution of 15.6ms. This was adding up to 15ms of dead time per cycle. The USB round-trip itself provides natural pacing.

2. **`FlushBuffer()` bypassed** — The defensive `FlushBuffer()` inside `ReadWrite()` calls `GetRxBytesAvailable()` (USB round-trip) and then `Read()` to drain stale bytes (another USB round-trip if bytes exist). In steady-state streaming this is pure overhead. The new `ReadWriteNoFlush()` method eliminates this.

3. **Debug output reduced** — `System.Diagnostics.Debug.WriteLine` every 10th cycle added measurable overhead. Relaxed to every 100th cycle.

### 10.5 USB Bus Contention

USB 2.0 High-Speed (480 Mbit/s) is shared among all devices on the same host controller:
- Each 1ms USB frame is divided into 8 microframes (125µs each)
- Bulk transfers (used by FTDI) get scheduled into available microframe slots
- Other devices (mouse, keyboard, audio, webcam) consume slots and add latency
- A busy USB hub can add 1-3ms per transaction

**Mitigation**: Plug the FT2232H into a USB port on a **dedicated host controller**. Front and rear USB ports on most PCs use different controllers. Check Windows Device Manager ? Universal Serial Bus controllers to verify. Each "Host Controller" entry is an independent bus.

**Expected impact**: On a shared USB bus with active devices, round-trip times can be 30-50% higher. A dedicated controller should deliver the best-case numbers consistently.

### 10.6 SPI Bus Theoretical Maximum

At 30 MHz clock, 8 bits per byte:

| Metric | Value @ 30 MHz | Value @ 15 MHz |
|--------|---------------|----------------|
| Raw bit rate | 30,000,000 bits/sec | 15,000,000 bits/sec |
| Raw byte rate | 3,750,000 bytes/sec | 1,875,000 bytes/sec |
| At 2 bytes/sample, 1 channel | 1,875,000 sps | 937,500 sps |
| At 2 bytes/sample, 3 channels | 625,000 sps/channel | 312,500 sps/channel |

These are theoretical ceilings. In practice, the USB round-trip is the limiter, not the SPI bus.

### 10.7 Measured Cycle Rate

**Measured**: ~300 cycles/sec peak, often slower depending on USB bus conditions.

This corresponds to ~3.3ms per cycle, which is consistent with:
- `FtdiDevice.Write()`: ~1-2ms (USB write: MPSSE command + TX data)
- `FtdiDevice.Read()`: ~1-2ms (USB read: wait for SPI completion + RX data)
- Total: ~2-4ms, averaging ~3.3ms

The `FlushBuffer()` removal and `Thread.Sleep(1)` removal brought the cycle rate up from ~250 to ~300. Further improvement requires reducing the number of USB operations per cycle, which is already at the minimum of 2 (write + read).

### 10.8 Realistic Throughput (USB-Limited, 4K-Clamped)

With 300 cycles/sec and each transfer clamped to 4094 data bytes:

**Maximum throughput** = 300 cycles/sec × 4094 bytes/cycle = **1,228,200 bytes/sec ? 1.2 MB/s**

| Config | Bytes/Frame | Max Frames/Cycle | Max Throughput | Max Sample Rate |
|--------|-------------|------------------|----------------|-----------------|
| 3ch, int16, markers | 8 | 511 | 1,228,200 B/s | **153 ksps total** |
| 1ch, int16, markers | 4 | 1023 | 1,228,200 B/s | **307 ksps** |
| 1ch, int16, no markers | 2 | 2047 | 1,228,200 B/s | **614 ksps** |

**Detailed breakdown for 3 channels (8 bytes/frame):**

| Sample Rate | Bytes/Cycle (in 3.3ms) | Throughput | Buffer Fills 4K? |
|-------------|------------------------|------------|------------------|
| 1 kHz | 27 | 8,000 B/s | No (27B) |
| 10 kHz | 267 | 80,000 B/s | No (267B) |
| 25 kHz | 667 | 200,000 B/s | No (667B) |
| 50 kHz | 1,333 | 400,000 B/s | No (1,333B) |
| 100 kHz | 2,667 | 800,000 B/s | No (2,667B) |
| 125 kHz | 3,333 | 1,000,000 B/s | No (3,333B) |
| **153 kHz** | **4,094** | **1,228,200 B/s** | **Yes (4,094B = max)** |

**Detailed breakdown for 1 channel (2 bytes/sample, no markers):**

| Sample Rate | Bytes/Cycle (in 3.3ms) | Throughput | Buffer Fills 4K? |
|-------------|------------------------|------------|------------------|
| 10 kHz | 67 | 20,000 B/s | No |
| 100 kHz | 667 | 200,000 B/s | No |
| 250 kHz | 1,667 | 500,000 B/s | No |
| 500 kHz | 3,333 | 1,000,000 B/s | No |
| **614 kHz** | **4,094** | **1,228,200 B/s** | **Yes (max)** |

### 10.9 Ring Buffer Sizing (PSoC Side)

The PSoC ring buffer must hold at least one cycle's worth of data to prevent overflow. At 300 cycles/sec the average cycle is 3.3ms, but worst case can be 5-10ms during USB congestion:

| Sample Rate (3ch, 8B/frame) | Bytes in 3.3ms | Bytes in 10ms (worst) | Recommended Buffer |
|------------------------------|----------------|----------------------|-------------------|
| 1 kHz | 27 | 80 | 512 |
| 10 kHz | 267 | 800 | 4,096 |
| 25 kHz | 667 | 2,000 | 8,192 |
| 50 kHz | 1,333 | 4,000 | 16,384 |
| 100 kHz | 2,667 | 8,000 | 32,768 |
| 153 kHz | 4,094 | 12,240 | 49,152 |

**Current ring buffer size (4,096 bytes)** handles up to ~10 kHz (3 channels) with worst-case headroom. For higher sample rates, increase `RING_BUFFER_SIZE`. The PSoC has plenty of SRAM for 32K-64K buffers.

### 10.10 Why 30 MHz Still Helps

Even though USB is the bottleneck, running SPI at 30 MHz instead of 15 MHz is beneficial:

| Metric | @ 15 MHz | @ 30 MHz |
|--------|----------|----------|
| SPI time for 4094 bytes | 2.2 ms | 1.1 ms |
| USB overhead per cycle | ~2-3 ms | ~2-3 ms |
| Total cycle time | ~4-5 ms | ~3-4 ms |
| **Cycles/sec** | **~200-250** | **~250-330** |

The faster SPI clock shrinks the SPI portion of each cycle, leaving more time for USB. This translates to roughly **25-30% more cycles/sec**, which directly increases throughput.

### 10.11 Summary: What to Expect

Based on **measured 300 cycles/sec** with optimized code (no Sleep, no FlushBuffer):

| Configuration | Max Sample Rate | Throughput | Limiting Factor |
|--------------|-----------------|------------|-----------------|
| 3 channels, int16, with markers (8B/frame) | **~153 ksps total** | 1.2 MB/s | 4K transfer clamp |
| 1 channel, int16, with markers (4B/frame) | **~307 ksps** | 1.2 MB/s | 4K transfer clamp |
| 1 channel, int16, no markers (2B/frame) | **~614 ksps** | 1.2 MB/s | 4K transfer clamp |
| Any config, USB worst case | Divide above by ~2 | ~600 KB/s | USB congestion |
| Theoretical SPI max (30 MHz, no USB) | 1.875 Msps (1ch) | 3.75 MB/s | SPI wire speed |

**Key takeaways:**
1. The USB round-trip limits cycle rate to **~300/sec** (measured)
2. The 4K RX buffer clamps each cycle to 4094 data bytes
3. Combined ceiling: **~1.2 MB/s sustained throughput**
4. 30 MHz SPI helps by ~25-30% over 15 MHz (less dead time per cycle)
5. The PSoC should sample at full speed and let the ring buffer absorb — the master drains in 4K chunks
6. For sample rates above the 4K limit, consider decimation on the PSoC side
