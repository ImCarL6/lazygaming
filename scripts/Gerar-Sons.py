"""Sinais originais de ativação/desativação. PCM estéreo; sem samples externos."""
from pathlib import Path
import math
import random
import struct
import wave

output = Path(__file__).resolve().parents[1] / 'tv' / 'assets' / 'audio'
output.mkdir(parents=True, exist_ok=True)
rate = 44100
for mode, notes in [('on', [(659.25, 0, .12), (987.77, .055, .16), (1318.51, .09, .12)]), ('off', [(987.77, 0, .12), (659.25, .055, .16)])]:
    raw = bytearray()
    noise = random.Random(17)
    for index in range(int(.27 * rate)):
        time = index / rate
        sample = 0.0
        for frequency, start, length in notes:
            age = time - start
            if 0 <= age < length:
                envelope = min(1, age / .004) * math.exp(-age / (length * .27)) * min(1, (length - age) / .025)
                sample += (math.sin(2 * math.pi * frequency * age) + .20 * math.sin(2 * math.pi * frequency * 2 * age) + .06 * math.sin(2 * math.pi * frequency * 3 * age)) * envelope * .12
        sample += (noise.random() * 2 - 1) * .004 * math.exp(-time / .009)
        value = round(max(-1, min(1, sample)) * 32767)
        raw += struct.pack('<hh', value, value)
    with wave.open(str(output / f'mouse-{mode}.wav'), 'wb') as audio:
        audio.setnchannels(2)
        audio.setsampwidth(2)
        audio.setframerate(rate)
        audio.writeframes(raw)
print('Sinais originais gerados: mouse-on.wav e mouse-off.wav')
