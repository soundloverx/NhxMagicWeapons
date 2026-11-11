# Makes Assets\muspel_channel_sound from two Mixkit sounds the user picked (2026-11-10), kept in Assets\Sounds\: the
# Wrath of Muspelheim's channelled beam's sound (Staves\WrathOfMuspelheim\Effects\ChannelSound.cs). Embedded in the DLL.
#
#   python Tools\make_channel_sound.py Assets\Sounds\mixkit-long-fire-beam-1336.wav Assets\Sounds\mixkit-aggressive-fire-flame-1333.wav Assets\muspel_channel_sound
#
# Needs numpy. Neither sound loops, but each holds a steady roar in its middle: each is cut into an intro (its start, up
# to its loop), a loop (its steady middle, its last LOOP_FADE seconds cross-faded, equal power, into its start, so that
# it runs on into itself and the intro runs on into it) and a tail (its own ending, after its loop). The beam's steady
# part is 1.0 to 6.75 s (the loop 5.25 s), the flame's 1.75 to 5.25 s (3.0 s): different lengths, so they line up
# differently each time round. Mono (the game places the sound at the staff: a stereo clip would be played as one point
# anyway), 16-bit, 44.1 kHz, 6 dB louder than the files (see GAIN_DB). The user approved this cut as a preview, with the flame half as loud under the beam, both
# pulsing with the thread and swelling at each hit ("Oh the pulsing one sounds amazing!"); the mixing and pulsing are the
# game's (ChannelSound).
#
# The file (raw deflate, little-endian): "NHXS", version (u16), sample rate (u32), count (u16), then for each: name (u16
# length, UTF-8), sample count (u32), samples (i16). Names: beam_intro, beam_loop, beam_tail, flame_intro, flame_loop,
# flame_tail.
import struct
import sys
import wave
import zlib

import numpy as np

RATE = 44100
LOOP_FADE = 0.5
# 6 dB louder than the files (the user, 2026-11-10: "I believe the volume can be turned up a little bit, like 6dB?"):
# baked in, as a sound source's volume can't go over 1 and the game's swells already take it to 0.91. The beam then
# peaks over full scale, so a limiter holds its peaks under CEILING: its gain drops ahead of a peak (LOOKAHEAD) and comes
# back over RELEASE; it only acts on the loudest moments (the beam's 99.99th percentile was 0.62 of full scale)
GAIN_DB = 6.0
CEILING = 0.95
LOOKAHEAD = 0.003
RELEASE = 0.06
# (name, loop start, loop end) in seconds, for the beam's file and the flame's
CUTS = [("beam", 1.0, 6.75), ("flame", 1.75, 5.25)]


def read(path):
    with wave.open(path, "rb") as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    if rate != RATE:
        raise SystemExit(f"{path}: {rate} Hz, not {RATE}")
    if width == 3:
        b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
        a = b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16)
        a = np.where(a & 0x800000, a - 0x1000000, a) / 2 ** 23
    elif width == 2:
        a = np.frombuffer(raw, "<i2") / 2 ** 15
    else:
        raise SystemExit(f"{path}: {width * 8}-bit samples")
    return a.reshape(-1, channels).mean(axis=1)


def seconds(t):
    return int(round(t * RATE))


# GAIN_DB louder, its peaks held under CEILING
def louder(x):
    x = x * 10 ** (GAIN_DB / 20)
    need = np.minimum(1.0, CEILING / np.maximum(np.abs(x), 1e-9))
    w = max(1, seconds(LOOKAHEAD))
    # The lowest gain needed over the next w samples, so that it's down before the peak
    ahead = np.lib.stride_tricks.sliding_window_view(np.concatenate([need, np.ones(w)]), w + 1).min(axis=1)
    gain = np.empty_like(ahead)
    current = 1.0
    back = np.exp(-1.0 / (RELEASE * RATE))
    for i, g in enumerate(ahead):
        current = g if g < current else back * current + (1 - back) * g
        gain[i] = current
    # Smoothed, so that it doesn't click as it drops
    gain = np.convolve(gain, np.ones(w) / w, mode="same")
    return x * gain


def cut(x, start, end):
    a, b, c = seconds(start), seconds(end), seconds(LOOP_FADE)
    t = np.linspace(0, np.pi / 2, c)
    loop = x[a + c:b].copy()
    loop[-c:] = x[b - c:b] * np.cos(t) + x[a:a + c] * np.sin(t)
    return x[:a + c], loop, x[b:]


def main(beam_path, flame_path, dst):
    entries = []
    for (name, start, end), path in zip(CUTS, (beam_path, flame_path)):
        parts = cut(louder(read(path)), start, end)
        for part, samples in zip(("intro", "loop", "tail"), parts):
            clipped = np.abs(samples) > 0.999
            data = (np.clip(samples, -0.999, 0.999) * 32767).round().astype("<i2").tobytes()
            key = f"{name}_{part}".encode("utf-8")
            entries.append(struct.pack("<H", len(key)) + key + struct.pack("<I", len(samples)) + data)
            rms = 20 * np.log10(np.sqrt((samples ** 2).mean()))
            print(f"  {name}_{part}: {len(samples) / RATE:.2f} s, peak {np.abs(samples).max():.3f}, {rms:.1f} dBFS, "
                  f"{clipped.sum()} samples clipped")
        # The loop's seam: no step bigger than the sound's own
        loop = parts[1]
        steps = np.abs(np.diff(loop))
        seam = abs(loop[0] - loop[-1])
        if seam > np.percentile(steps, 99.9):
            raise SystemExit(f"{name}'s loop jumps by {seam:.4f} at its seam")
    raw = b"NHXS" + struct.pack("<HIH", 1, RATE, len(entries)) + b"".join(entries)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = packer.compress(raw) + packer.flush()
    open(dst, "wb").write(out)
    print(f"Wrote {len(entries)} sounds, {len(raw):,} bytes, {len(out):,} deflated, to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("usage: make_channel_sound.py <long fire beam .wav> <aggressive fire flame .wav> <output file>")
    main(*sys.argv[1:4])
