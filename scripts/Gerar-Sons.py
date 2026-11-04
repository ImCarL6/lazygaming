"""Sinais originais do LazyGaming. PCM estéreo; sem samples externos.

mouse-on/off: ativação do mouse (inalterados).
launcher-open/close: abrir e fechar o launcher. Começam no primeiro sample
(sem silêncio inicial) para coincidir com o primeiro quadro visível.
"""
from pathlib import Path
import math
import random
import struct
import wave

output = Path(__file__).resolve().parents[1] / 'tv' / 'assets' / 'audio'
output.mkdir(parents=True, exist_ok=True)
rate = 44100


def write(name, left, right=None):
    right = right or left
    raw = bytearray()
    for a, b in zip(left, right):
        raw += struct.pack('<hh', round(max(-1, min(1, a)) * 32767), round(max(-1, min(1, b)) * 32767))
    with wave.open(str(output / name), 'wb') as audio:
        audio.setnchannels(2)
        audio.setsampwidth(2)
        audio.setframerate(rate)
        audio.writeframes(raw)


# Kit original do mouse (mesmos parâmetros; saída idêntica).
for mode, notes in [('on', [(659.25, 0, .12), (987.77, .055, .16), (1318.51, .09, .12)]), ('off', [(987.77, 0, .12), (659.25, .055, .16)])]:
    samples = []
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
        samples.append(sample)
    write(f'mouse-{mode}.wav', samples)


def launcher(notes, sweep, length, gain, seed):
    """Clique macio + corpo grave em glissando + campainha curta com leve abertura estéreo."""
    noise = random.Random(seed)
    left, right = [], []
    previous = 0.0
    phase = 0.0
    for index in range(int(length * rate)):
        time = index / rate
        # Clique de toque: ruído suavizado, 2,5 ms.
        raw = noise.random() * 2 - 1
        previous += (raw - previous) * .35
        click = previous * .05 * math.exp(-time / .0025)
        # Corpo: seno com glissando exponencial, decai em ~70 ms.
        start, end, duration = sweep
        progress = min(1, time / duration)
        frequency = start * (end / start) ** progress
        phase += 2 * math.pi * frequency / rate
        body = math.sin(phase) * .11 * min(1, time / .003) * math.exp(-time / .07)
        tones = [0.0, 0.0]
        for frequency, begin, size, level in notes:
            age = time - begin
            if 0 <= age < size:
                envelope = min(1, age / .003) * math.exp(-age / (size * .3)) * min(1, (size - age) / .03)
                for channel, detune in enumerate((1.0, 1.0025)):
                    f = frequency * detune
                    tones[channel] += (math.sin(2 * math.pi * f * age) + .30 * math.sin(2 * math.pi * f * 2 * age) * math.exp(-age / .04)
                                       + .07 * math.sin(2 * math.pi * f * 3 * age)) * envelope * level
        fade = min(1, (length - time) / .02)
        left.append((click + body + tones[0]) * gain * fade)
        right.append((click + body + tones[1]) * gain * fade)
    return left, right


# Abrir: sobe G5 → D6 → G6, corpo grave sobe. Fechar: desce D6 → G5 → D5, mais discreto.
write('launcher-open.wav', *launcher([(783.99, 0, .20, .10), (1174.66, .045, .22, .09), (1567.98, .085, .26, .075)], (150, 300, .09), .36, 1.0, 23))
write('launcher-close.wav', *launcher([(1174.66, 0, .16, .085), (783.99, .04, .18, .08), (587.33, .08, .22, .07)], (280, 140, .1), .32, .85, 29))
print('Sinais originais gerados: mouse-on/off e launcher-open/close.')
