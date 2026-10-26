using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using WinRT;

namespace Mizon.Windows;


/// <summary>
/// Private interface used for memory buffer access for software bitmap
/// </summary>
[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

public static class BlurHash
{


    struct Pixel
    {
        public float R;
        public float G;
        public float B;
    }

    const string Base83Chars =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    static int Decode83(ReadOnlySpan<char> str)
    {
        int value = 0;
        foreach (var c in str)
            value = value * 83 + Base83Chars.IndexOf(c);
        return value;
    }

    static void Encode83(int value, Span<char> buffer)
    {
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            buffer[i] = Base83Chars[value % 83];
            value /= 83;
        }
    }

    static float SrgbToLinear(int value)
    {
        float v = value / 255f;
        return v <= 0.04045f
            ? v / 12.92f
            : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    static int LinearToSrgb(float value)
    {
        value = Math.Clamp(value, 0, 1);

        float v = value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

        return (int)Math.Clamp(v * 255 + 0.5f, 0, 255);
    }

    static float SignPow(float value, float exp)
    {
        return MathF.CopySign(MathF.Pow(MathF.Abs(value), exp), value);
    }

    static Pixel[] ConvertBitmap(SoftwareBitmap bitmap)
    {
        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;

        Pixel[] pixels = new Pixel[w * h];

        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();

        unsafe
        {
         

            var byteAccess = reference.As<IMemoryBufferByteAccess>();

            byte* dataInBytes;
            uint capacity;
            byteAccess.GetBuffer(out dataInBytes, out capacity);


            Parallel.For(0, h, y =>
            {
                int index = y * w * 4;

                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = new Pixel
                    {
                        B = SrgbToLinear(dataInBytes[index]),
                        G = SrgbToLinear(dataInBytes[index + 1]),
                        R = SrgbToLinear(dataInBytes[index + 2])
                    };

                    index += 4;
                }
            });
        }

        return pixels;
    }

    public static string Encode(SoftwareBitmap bitmap, int compX = 4, int compY = 3)
    {
        var pixels = ConvertBitmap(bitmap);

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        Pixel[] factors = new Pixel[compX * compY];

        Parallel.For(0, compY, yComp =>
        {
            for (int xComp = 0; xComp < compX; xComp++)
            {
                float r = 0, g = 0, b = 0;

                float norm = (xComp == 0 && yComp == 0) ? 1 : 2;

                for (int y = 0; y < height; y++)
                {
                    float cosY = MathF.Cos(MathF.PI * yComp * y / height);

                    for (int x = 0; x < width; x++)
                    {
                        float cosX = MathF.Cos(MathF.PI * xComp * x / width);
                        float basis = cosX * cosY;

                        var p = pixels[y * width + x];

                        r += basis * p.R;
                        g += basis * p.G;
                        b += basis * p.B;
                    }
                }

                float scale = norm / (width * height);

                factors[yComp * compX + xComp] = new Pixel
                {
                    R = r * scale,
                    G = g * scale,
                    B = b * scale
                };
            }
        });

        Span<char> buffer = stackalloc char[4 + 2 * compX * compY];

        int sizeFlag = (compX - 1) + (compY - 1) * 9;
        Encode83(sizeFlag, buffer.Slice(0, 1));

        float max = 0;

        for (int i = 1; i < factors.Length; i++)
        {
            max = Math.Max(max, Math.Abs(factors[i].R));
            max = Math.Max(max, Math.Abs(factors[i].G));
            max = Math.Max(max, Math.Abs(factors[i].B));
        }

        int quantMax = (int)Math.Clamp(MathF.Floor(max * 166 - 0.5f), 0, 82);
        float realMax = (quantMax + 1) / 166f;

        Encode83(quantMax, buffer.Slice(1, 1));

        var dc = factors[0];

        int dcValue =
            (LinearToSrgb(dc.R) << 16) |
            (LinearToSrgb(dc.G) << 8) |
            LinearToSrgb(dc.B);

        Encode83(dcValue, buffer.Slice(2, 4));

        for (int i = 1; i < factors.Length; i++)
        {
            var f = factors[i];

            int r = (int)Math.Clamp(MathF.Floor(SignPow(f.R / realMax, 0.5f) * 9 + 9.5f), 0, 18);
            int g = (int)Math.Clamp(MathF.Floor(SignPow(f.G / realMax, 0.5f) * 9 + 9.5f), 0, 18);
            int b = (int)Math.Clamp(MathF.Floor(SignPow(f.B / realMax, 0.5f) * 9 + 9.5f), 0, 18);

            int ac = r * 19 * 19 + g * 19 + b;

            Encode83(ac, buffer.Slice(4 + (i - 1) * 2, 2));
        }

        return buffer.ToString();
    }

    public static SoftwareBitmap Decode(string hash, int width, int height, float punch = 1)
    {
        int sizeFlag = Decode83(hash.AsSpan(0, 1));

        int compX = sizeFlag % 9 + 1;
        int compY = sizeFlag / 9 + 1;

        int quantMax = Decode83(hash.AsSpan(1, 1));
        float maxVal = (quantMax + 1) / 166f;

        Pixel[] colors = new Pixel[compX * compY];

        int dc = Decode83(hash.AsSpan(2, 4));

        colors[0] = new Pixel
        {
            R = SrgbToLinear(dc >> 16),
            G = SrgbToLinear((dc >> 8) & 255),
            B = SrgbToLinear(dc & 255)
        };

        for (int i = 1; i < colors.Length; i++)
        {
            int ac = Decode83(hash.AsSpan(4 + (i - 1) * 2, 2));

            int r = ac / (19 * 19);
            int g = (ac / 19) % 19;
            int b = ac % 19;

            colors[i] = new Pixel
            {
                R = SignPow((r - 9) / 9f, 2) * maxVal * punch,
                G = SignPow((g - 9) / 9f, 2) * maxVal * punch,
                B = SignPow((b - 9) / 9f, 2) * maxVal * punch
            };
        }

        byte[] result = new byte[width * height * 4];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0;

                for (int j = 0; j < compY; j++)
                    for (int i = 0; i < compX; i++)
                    {
                        float basis =
                            MathF.Cos(MathF.PI * x * i / width) *
                            MathF.Cos(MathF.PI * y * j / height);

                        var c = colors[j * compX + i];

                        r += c.R * basis;
                        g += c.G * basis;
                        b += c.B * basis;
                    }

                int idx = (y * width + x) * 4;

                result[idx] = (byte)LinearToSrgb(b);
                result[idx + 1] = (byte)LinearToSrgb(g);
                result[idx + 2] = (byte)LinearToSrgb(r);
                result[idx + 3] = 255;
            }
        });

        var bmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

        using var buffer = bmp.LockBuffer(BitmapBufferAccessMode.Write);
        using var reference = buffer.CreateReference();

        unsafe
        {
       
            var byteAccess = reference.As<IMemoryBufferByteAccess>();

            byte* dataInBytes;
            uint capacity;
            byteAccess.GetBuffer(out dataInBytes, out capacity);


            Marshal.Copy(result, 0, (IntPtr)dataInBytes, result.Length);
        }

        return bmp;
    }
}
