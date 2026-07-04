"""
Extract Stardew Valley XNB texture to PNG.
Handles both uncompressed and LZ4-compressed XNB files.
"""
import struct
import sys
import os

def read_7bit_encoded_int(data, offset):
    """Read a 7-bit encoded integer."""
    result = 0
    shift = 0
    while True:
        byte = data[offset]
        offset += 1
        result |= (byte & 0x7F) << shift
        shift += 7
        if (byte & 0x80) == 0:
            break
    return result, offset

def decompress_lz4(data):
    """Decompress LZ4 data."""
    result = bytearray()
    pos = 0
    while pos < len(data):
        token = data[pos]
        pos += 1
        lit_len = token >> 4
        if lit_len == 15:
            while True:
                b = data[pos]
                pos += 1
                lit_len += b
                if b != 255:
                    break
        result.extend(data[pos:pos + lit_len])
        pos += lit_len
        if pos >= len(data):
            break
        offset = data[pos] | (data[pos + 1] << 8)
        pos += 2
        match_len = (token & 0x0F) + 4
        if (token & 0x0F) == 15:
            while True:
                b = data[pos]
                pos += 1
                match_len += b
                if b != 255:
                    break
        # Copy from already-decompressed data
        match_pos = len(result) - offset
        for i in range(match_len):
            result.append(result[match_pos + i])
    return bytes(result)

def extract_xnb_to_png(xnb_path, output_path):
    with open(xnb_path, 'rb') as f:
        data = f.read()

    # Parse header
    magic = data[0:3]
    if magic != b'XNB':
        print(f"ERROR: Not an XNB file: {xnb_path}")
        return False

    platform = data[3]  # 'w', 'm', 'i', 'a'
    version = data[4]
    flags = data[5]

    print(f"Platform: {chr(platform)}, Version: {version}, Flags: {flags}")

    is_compressed_lz4 = (flags & 0x40) != 0
    is_compressed_lzx = (flags & 0x80) != 0

    if is_compressed_lzx:
        print("ERROR: LZX compression not supported")
        return False

    if is_compressed_lz4:
        compressed_size = struct.unpack('<I', data[6:10])[0]
        decompressed_size = struct.unpack('<I', data[10:14])[0]
        print(f"Compressed: {compressed_size} -> {decompressed_size} bytes")
        compressed = data[14:14 + compressed_size]
        content = decompress_lz4(compressed)
        offset = 0
    else:
        content = data[6:]
        offset = 0

    # Read reader count
    reader_count, offset = read_7bit_encoded_int(content, offset)
    print(f"Reader count: {reader_count}")

    for i in range(reader_count):
        # Read type reader name
        name_len, offset = read_7bit_encoded_int(content, offset)
        reader_name = content[offset:offset + name_len].decode('utf-8')
        offset += name_len
        print(f"  Reader {i}: {reader_name}")

        # Read version
        reader_version = struct.unpack_from('<i', content, offset)[0]
        offset += 4

        if 'Texture2DReader' in reader_name:
            # Read surface format
            fmt = struct.unpack_from('<i', content, offset)[0]
            offset += 4
            width = struct.unpack_from('<i', content, offset)[0]
            offset += 4
            height = struct.unpack_from('<i', content, offset)[0]
            offset += 4
            mip_count = struct.unpack_from('<i', content, offset)[0]
            offset += 4
            data_size = struct.unpack_from('<i', content, offset)[0]
            offset += 4

            print(f"  Texture: {width}x{height}, format={fmt}, mips={mip_count}, data_size={data_size}")

            # SurfaceFormat: 0=Color (RGBA), 1=Bgr565, 2=Bgra5551, 3=Bgra4444,
            #                4=Dxt1, 5=Dxt3, 6=Dxt5, 7=NormalizedByte2, 8=NormalizedByte4,
            #                9=Rgba1010102, 10=Rg32, 11=Rgba64, 12=Alpha8, 13=Single,
            #                14=Vector2, 15=Vector4, 16=HalfSingle, 17=HalfVector2, 18=HalfVector4

            if fmt == 0:  # Color (RGBA8)
                pixel_data = content[offset:offset + data_size]
                # RGBA -> PNG
                from PIL import Image
                img = Image.frombytes('RGBA', (width, height), pixel_data)
                # XNA uses premultiplied alpha; we can use it as-is for visualization
                img.save(output_path)
                print(f"Saved to: {output_path}")
                return True
            elif fmt == 28:  # DXT3 / BC2
                pixel_data = content[offset:offset + data_size]
                from PIL import Image
                # Simple BC2 decoder
                img_data = bytearray(width * height * 4)
                block_idx = 0
                for by in range(0, height, 4):
                    for bx in range(0, width, 4):
                        alpha_data = struct.unpack_from('<Q', pixel_data, block_idx)[0]
                        block_idx += 8
                        c0 = struct.unpack_from('<H', pixel_data, block_idx)[0]
                        block_idx += 2
                        c1 = struct.unpack_from('<H', pixel_data, block_idx)[0]
                        block_idx += 2

                        # Decode colors
                        def rgb565_to_rgba(c):
                            r = ((c >> 11) & 0x1F) * 255 // 31
                            g = ((c >> 5) & 0x3F) * 255 // 63
                            b = (c & 0x1F) * 255 // 31
                            return (r, g, b, 255)

                        color0 = rgb565_to_rgba(c0)
                        color1 = rgb565_to_rgba(c1)
                        colors = [color0, color1]
                        if c0 > c1:
                            colors.append(((2*color0[0]+color1[0])//3, (2*color0[1]+color1[1])//3, (2*color0[2]+color1[2])//3, 255))
                            colors.append(((color0[0]+2*color1[0])//3, (color0[1]+2*color1[1])//3, (color0[2]+2*color1[2])//3, 255))
                        else:
                            colors.append(((color0[0]+color1[0])//2, (color0[1]+color1[1])//2, (color0[2]+color1[2])//2, 255))
                            colors.append((0, 0, 0, 0))

                        indices = struct.unpack_from('<I', pixel_data, block_idx)[0]
                        block_idx += 4

                        for y in range(4):
                            for x in range(4):
                                px = bx + x
                                py = by + y
                                if px < width and py < height:
                                    alpha = (alpha_data >> (4 * (y * 4 + x))) & 0xF
                                    alpha = alpha * 255 // 15
                                    idx = (indices >> (2 * (y * 4 + x))) & 3
                                    c = colors[idx]
                                    out_idx = (py * width + px) * 4
                                    img_data[out_idx] = c[0]
                                    img_data[out_idx+1] = c[1]
                                    img_data[out_idx+2] = c[2]
                                    img_data[out_idx+3] = alpha

                img = Image.frombytes('RGBA', (width, height), bytes(img_data))
                img.save(output_path)
                print(f"Saved to: {output_path}")
                return True
            else:
                print(f"Unsupported surface format: {fmt}")
                return False

    print("No texture found in XNB")
    return False

if __name__ == '__main__':
    xnb_path = r"D:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Content\LooseSprites\Cursors.xnb"
    output_path = r"D:\Project\OldFarmer\cursors_extracted.png"
    extract_xnb_to_png(xnb_path, output_path)
