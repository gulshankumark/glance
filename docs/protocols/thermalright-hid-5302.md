# Thermalright HID 0416:5302

Status: verified on real hardware (1280x480 panel).

## Device identity

- USB composite device named "USBDISPLAY", VID `0x0416`, PID `0x5302`.
- Interface 0: vendor-defined HID. Input report is 37 bytes including the report ID; output report is 513 bytes including the report ID.
- Interface 1: WinUSB bulk interface. Not used.

## Writing

Every write is one HID output report: report ID `0x00` followed by 512 payload bytes. A payload longer than 512 bytes is split into 512-byte chunks. The last chunk is zero-padded to 512 bytes.

## Handshake

Send this 20-byte message (chunked and zero-padded like any payload):

```
DA DB DC DD 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00
```

The device answers with an input report. Byte 0 is the report ID (`00`).

| Offset | Meaning |
|---|---|
| 1-4 | magic `DA DB DC DD` |
| 5 | sub-model |
| 6 | model code |
| 13 | must be `01` |
| 17 | `0x10` when a serial number is present |
| 21-36 | 16-byte serial number (when byte 17 is `0x10`) |

Reject the reply if the magic or byte 13 does not match. Example reply from a tested unit:
model `0x80`, sub-model `0x01`, serial `4250323139343007011D334247022078`.

## Model table

| Model | Sub-model | Panel | Frame format |
|---|---|---|---|
| `0x80` | `<= 0x02` | 1280x480 | JPEG |

Any other combination is unknown. Report it to the user as an unsupported model, including both codes.

## Frame

A 20-byte header followed by the JPEG bytes, then chunked as described above.

| Offset | Size | Value |
|---|---|---|
| 0 | 4 | `DA DB DC DD` |
| 4 | 1 | `02` = JPEG |
| 5 | 3 | `00 00 00` |
| 8 | 2 | width, u16 little-endian (1280) |
| 10 | 2 | height, u16 little-endian (480) |
| 12 | 1 | `02` |
| 13 | 3 | `00 00 00` |
| 16 | 4 | JPEG length, u32 little-endian |

## Keep-alive and limits

- The screen shows its built-in logo shortly after frames stop. Sending at least one frame per second keeps the image on (verified at 1 fps without flicker).
- Keep each JPEG below about 450 KB; start at quality 90 and step down when larger.
- Verified throughput: 24 fps of roughly 60 KB JPEGs at about 2.8% of one CPU core.
- The unit may be mounted upside down; rotation is applied by the renderer, not the protocol.
