"""Regenerate Mizon's original grab/grabbing cursors using only Python's standard library."""
from pathlib import Path
import math
import struct

ROOT = Path(__file__).parent
SHAPES = {
    'grab': {
        'outline': [('M', 10, 27), ('C', 8, 25, 6.5, 23, 5.5, 21), ('L', 2.6, 15.7),
                    ('C', 1.4, 13.5, 3.8, 12, 5.1, 13.9), ('L', 7.6, 17), ('L', 7.4, 7),
                    ('C', 7.4, 4.7, 10.2, 4.7, 10.3, 7), ('L', 10.6, 13.8), ('L', 11.4, 3.6),
                    ('C', 11.6, 1.3, 14.6, 1.5, 14.4, 3.8), ('L', 14.1, 13.5), ('L', 16.1, 5),
                    ('C', 16.6, 2.9, 19.3, 3.6, 18.9, 5.7), ('L', 17.5, 14.5), ('L', 20.7, 8.6),
                    ('C', 21.7, 6.7, 24.2, 8.1, 23.3, 10), ('L', 20.8, 17),
                    ('C', 20.2, 21, 19.2, 24, 18, 27), ('Z',)],
        'lines': [[('M', 10.6, 16.2), ('L', 10.7, 19.3)], [('M', 14, 16), ('L', 13.9, 19.1)],
                  [('M', 17.2, 16.6), ('L', 16.6, 19.4)]]
    },
    'grabbing': {
        'outline': [('M', 10, 27), ('C', 8.4, 24.3, 6.5, 22, 5, 19), ('L', 3.8, 16.3),
                    ('C', 2.8, 13.9, 5.4, 12.3, 7, 14), ('L', 8.7, 15.8), ('L', 8.5, 9.5),
                    ('C', 8.4, 7, 11.5, 6.5, 11.7, 9), ('L', 11.8, 8),
                    ('C', 11.9, 5.5, 15, 5.5, 15.1, 8), ('L', 15.1, 8.5),
                    ('C', 15.1, 6.2, 18.2, 6.1, 18.4, 8.5), ('L', 18.5, 10.1),
                    ('C', 18.6, 8, 21.5, 8.1, 21.7, 10.7), ('L', 22, 16.3),
                    ('C', 22.3, 20.6, 20.5, 24.1, 18.9, 27), ('Z',)],
        'lines': [[('M', 11.7, 9), ('L', 11.9, 14)], [('M', 15.1, 8.5), ('L', 15.2, 14)],
                  [('M', 18.5, 10.1), ('L', 18.5, 14.6)],
                  [('M', 8.8, 15.9), ('C', 10.4, 14.7, 13, 15.1, 13.2, 17.1), ('L', 13.3, 19.5)]]
    }
}

def flatten(path):
    points = []
    for command in path:
        if command[0] in ('M', 'L'):
            points.append(command[1:])
        elif command[0] == 'C':
            x, y = points[-1]
            a, b, c, d, e, f = command[1:]
            for step in range(1, 17):
                t = step / 16
                u = 1 - t
                points.append((u*u*u*x + 3*u*u*t*a + 3*u*t*t*c + t*t*t*e,
                               u*u*u*y + 3*u*u*t*b + 3*u*t*t*d + t*t*t*f))
        elif command[0] == 'Z':
            points.append(points[0])
    return points

def distance(x, y, poly):
    minimum = math.inf
    for (ax, ay), (bx, by) in zip(poly, poly[1:]):
        dx, dy = bx - ax, by - ay
        n = dx*dx + dy*dy
        t = max(0, min(1, ((x-ax)*dx + (y-ay)*dy) / n)) if n else 0
        minimum = min(minimum, (x-ax-t*dx)**2 + (y-ay-t*dy)**2)
    return minimum

def inside(x, y, poly):
    result = False
    for (ax, ay), (bx, by) in zip(poly, poly[1:]):
        if (ay > y) != (by > y) and x < (bx-ax)*(y-ay)/(by-ay)+ax:
            result = not result
    return result

def render(shape, size):
    outline = flatten(shape['outline'])
    lines = [flatten(line) for line in shape['lines']]
    pixels = bytearray()
    # Four samples per pixel; edges remain smooth at standard and high DPI.
    for py in range(size):
        for px in range(size):
            alpha = color = 0
            for sy, sx in ((.25, .25), (.25, .75), (.75, .25), (.75, .75)):
                x, y = (px+sx)*32/size, (py+sy)*32/size
                edge = distance(x, y, outline) <= .64*.64
                if edge:
                    alpha += 255
                    color += 25
                elif inside(x, y, outline):
                    alpha += 255
                    color += 58 if any(distance(x, y, line) <= .43*.43 for line in lines) else 250
            if alpha:
                value = round(color * 255 / alpha)
                pixels += bytes((value, value, value, round(alpha/4)))
            else:
                pixels += b'\x00\x00\x00\x00'
    return pixels

def dib(pixels, size):
    stride = ((size + 31)//32)*4
    mask = bytearray(stride*size)
    for y in range(size):
        for x in range(size):
            if pixels[(y*size+x)*4+3] == 0:
                mask[(size-1-y)*stride + x//8] |= 128 >> (x%8)
    bitmap = b''.join(pixels[y*size*4:(y+1)*size*4] for y in reversed(range(size)))
    header = struct.pack('<IiiHHIIiiII', 40, size, size*2, 1, 32, 0, len(bitmap), 0, 0, 0, 0)
    return header + bitmap + mask

for name, shape in SHAPES.items():
    sizes = (32, 48, 64, 96, 128)
    images = [dib(render(shape, size), size) for size in sizes]
    offset = 6 + len(sizes)*16
    directory = bytearray(struct.pack('<HHH', 0, 2, len(sizes)))
    for size, data in zip(sizes, images):
        hotspot = size//2
        directory += struct.pack('<BBBBHHII', size, size, 0, 0, hotspot, hotspot, len(data), offset)
        offset += len(data)
    (ROOT / (name+'.cur')).write_bytes(directory + b''.join(images))
    commands = lambda p: ' '.join(command[0]+' '+ ' '.join(str(v) for v in command[1:]) for command in p)
    svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">\n'
    svg += f'<path d="{commands(shape["outline"])}" fill="#fafafa" stroke="#191919" stroke-width="1.28" stroke-linejoin="round"/>\n'
    for line in shape['lines']:
        svg += f'<path d="{commands(line)}" fill="none" stroke="#3a3a3a" stroke-width="0.86" stroke-linecap="round"/>\n'
    (ROOT / (name+'.svg')).write_text(svg+'</svg>\n', encoding='utf-8')
    print(f'{name}: {len(directory) + sum(map(len, images))} bytes; sizes {sizes}; centered hotspot')
