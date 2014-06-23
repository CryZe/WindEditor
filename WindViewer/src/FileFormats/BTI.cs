﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;
using WindViewer.Editor;

namespace WindViewer.FileFormats
{
    public class BTI : BaseArchiveFile
    {
#region File Formats
        public enum ImageFormat : byte
        {
                    //Bits per Pixel | Block Width | Block Height | Block Size | Type / Description
            I4 = 0x00,      // 4 | 8 | 8 | 32 | grey
            I8 = 0x01,      // 8 | 8 | 8 | 32 | grey
            IA4 = 0x02,     // 8 | 8 | 4 | 32 | grey + alpha
            IA8 = 0x03,     //16 | 4 | 4 | 32 | grey + alpha
            RGB565 = 0x04,  //16 | 4 | 4 | 32 | color
            RGB5A3 = 0x05,  //16 | 4 | 4 | 32 | color + alpha
            RGBA32 = 0x06,  //32 | 4 | 4 | 64 | color + alpha
            C4 = 0x08,      //4 | 8 | 8 | 32 | palette choices (IA8, RGB565, RGB5A3)
            C8 = 0x09,      //8, 8, 4, 32 | palette choices (IA8, RGB565, RGB5A3)
            C14X2 = 0x0a,   //16 (14 used) | 4 | 4 | 32 | palette (IA8, RGB565, RGB5A3)
            CMPR = 0x0e,    //4 | 8 | 8 | 32 | mini palettes in each block, RGB565 or transparent.
        }

        public enum WrapMode : byte
        {
            ClampToEdge = 0,
            Repeat = 1,
            MirroredRepeat = 2,
        }

        public enum PaletteFormat : byte
        {
            IA8 = 0x00,
            RGB565 = 0x01,
            RGB5A3 = 0x02,
        }

        public class Palette
        {
            private byte[] _paletteData;

            public void Load(byte[] data, uint paletteEntryCount, int offset)
            {
                //Files that don't have palettes have an entry count of zero.
                if (paletteEntryCount == 0)
                {
                    _paletteData = new byte[0];
                    return;
                }

                _paletteData = new byte[paletteEntryCount*2];
                Array.Copy(data, offset, _paletteData, 0, (int) paletteEntryCount*2);
            }

            public byte[] GetBytes()
            {
                return _paletteData;
            }
        }

        public class FileHeader
        {
            private ImageFormat _format;
            private byte _alphaEnabled; //0 for no alpha, 0x02 (and anything else) for alpha enabled
            private ushort _width;
            private ushort _height;
            private WrapMode _wrapS; //0 or 1 (Clamp or Repeat?)
            private WrapMode _wrapT;
            private PaletteFormat _paletteFormat;
            private byte _unknown1;
            public ushort PaletteEntryCount { get; private set; } //Numer of entries in palette
            public int PaletteDataOffset; //Relative to file header
            private uint _unknown2; //0 in MKWii (RGBA for border?)
            private byte _filterSettingMin; //1 or 0? (Assumed filters are in min/mag order)
            private byte _filterSettingMag; //
            private ushort _padding1; //0 in MKWii. //Padding
            private byte _imageCount; //(= numMipmaps + 1)
            private byte _padding2; //0 in MKWii //Padding
            private ushort _padding3; //0 in MKWii
            public int ImageDataOffset; //Relative to file header

            public const int Size = 32;
            public void Load(byte[] data, uint offset)
            {
                _format = (ImageFormat)FSHelpers.Read8(data, (int)offset + 0x00);
                _alphaEnabled = FSHelpers.Read8(data, (int)offset + 0x01);
                _width = (ushort)FSHelpers.Read16(data, (int)offset + 0x02);
                _height = (ushort)FSHelpers.Read16(data, (int)offset + 0x04);
                _wrapS = (WrapMode)FSHelpers.Read8(data, (int)offset + 0x06);
                _wrapT = (WrapMode)FSHelpers.Read8(data, (int)offset + 0x07);
                _paletteFormat = (PaletteFormat)FSHelpers.Read8(data, (int)offset + 0x8);
                _unknown1 = FSHelpers.Read8(data, (int) offset + 0x09);
                PaletteEntryCount = (ushort)FSHelpers.Read16(data, (int)offset + 0xA);
                PaletteDataOffset = (int)FSHelpers.Read32(data, (int)offset + 0xC);
                _unknown2 = (uint)FSHelpers.Read32(data, (int)offset + 0x10);
                _filterSettingMin = FSHelpers.Read8(data, (int)offset + 0x14);
                _filterSettingMag = FSHelpers.Read8(data, (int)offset + 0x15);
                _padding1 = (ushort)FSHelpers.Read16(data, (int)offset + 0x16);
                _imageCount = FSHelpers.Read8(data, (int)offset + 0x18);
                _padding2 = FSHelpers.Read8(data, (int)offset + 0x19);
                _padding3 = (ushort)FSHelpers.Read16(data, (int)offset + 0x1A);
                ImageDataOffset = (int)FSHelpers.Read32(data, (int)offset + 0x1C);
            }

            public ImageFormat GetFormat()
            {
                return _format;
            }

            public uint GetWidth()
            {
                return _width;
            }

            public uint GetHeight()
            {
                return _height;
            }

            public PaletteFormat GetPaletteFormat()
            {
                return _paletteFormat;
            }
        }
#endregion


        public FileHeader Header;
        public Palette ImagePalette;
        
        /// <summary>
        /// Call this if you want to initialize a BTI file from a filestream on disk.
        /// </summary>
        /// <param name="data"></param>
        public override void Load(byte[] data)
        {
            _dataCache = data;

            Header = new FileHeader();
            Header.Load(data, 0);

            ImagePalette = new Palette();
            ImagePalette.Load(data, Header.PaletteEntryCount, Header.PaletteDataOffset);

            HerpDerp(data);
        }

        /// <summary>
        /// And call this one if you want to initialize it from inside a byte stream
        /// ie: Inside of a BMD/BDL file.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        public void Load(byte[] data, uint offset, int headerLocOffset)
        {
            Header = new FileHeader();
            Header.Load(data, offset);

            //We're going to modify the header's offsets by a second offset
            //because reasons.
            Header.ImageDataOffset += headerLocOffset;
            Header.PaletteDataOffset += headerLocOffset;


            ImagePalette = new Palette();
            ImagePalette.Load(data, Header.PaletteEntryCount, (int)offset + Header.PaletteDataOffset);

            //Copy a subsection out of the big buffer that is our actual file data.
            //This is temp until we have a ImageData struct I guess?
            //byte[] buffer = new byte[GetFileSize()];
            //Array.Copy(data, (int) offset, buffer, 0, GetFileSize());

            HerpDerp(data);
        }
        
        //The index offset is a hack until I figure out a better way to do (/ confirm)
        //that the indexes specified in the file header are actually relative to the
        //start of that particular header, and not to the start of the file itself, which
        //is kind of like wtf... Yeah so that seems to be the issue is that the indexes
        //specified in the header are relative to the start of the header, so we sort of
        //need to either modify the offsets... the offset probably also applies to the 
        //palettes but I haven't tested those. The best solution might be to pass the
        //offset to the loading function and on through to HerpDerp but that seems weird
        //Meh.
        private void HerpDerp(byte[] fileData)
        {
            switch (Header.GetFormat())
            {
                case ImageFormat.RGBA32:
                {
                    uint numBlocksW = Header.GetWidth() / 4; //4 byte block width
                    uint numBlocksH = Header.GetHeight() / 4; //4 byte block height 

                    int dataOffset = Header.ImageDataOffset;
                    byte[] destData = new byte[Header.GetWidth() * Header.GetHeight() * 4];

                    for (int yBlock = 0; yBlock < numBlocksH; yBlock++)
                    {
                        for (int xBlock = 0; xBlock < numBlocksW; xBlock++)
                        {
                            //For each block, we're going to examine block width / block height number of 'pixels'
                            for (int pY = 0; pY < 4; pY++)
                            {
                                for (int pX = 0; pX < 4; pX++)
                                {
                                    //Ensure the pixel we're checking is within bounds of the image.
                                    if ((xBlock * 4 + pX >= Header.GetWidth()) || (yBlock * 4 + pY >= Header.GetHeight()))
                                    {
                                        continue;
                                    }

                                    //Now we're looping through each pixel in a block, but a pixel is four bytes long. 
                                    uint destIndex = (uint)(4 * (Header.GetWidth() * ((yBlock * 4) + pY) + (xBlock * 4) + pX));
                                    destData[destIndex + 3] = fileData[dataOffset + 0]; //Alpha
                                    destData[destIndex + 2] = fileData[dataOffset + 1]; //Red
                                    dataOffset += 2;
                                }
                            }

                            //...but we have to do it twice, because RGBA32 stores two sub-blocks per block. (AR, and GB)
                            for (int pY = 0; pY < 4; pY++)
                            {
                                for (int pX = 0; pX < 4; pX++)
                                {
                                    //Ensure the pixel we're checking is within bounds of the image.
                                    if ((xBlock * 4 + pX >= Header.GetWidth()) || (yBlock * 4 + pY >= Header.GetHeight()))
                                    {
                                        continue;
                                    }

                                    //Now we're looping through each pixel in a block, but a pixel is four bytes long. 
                                    uint destIndex = (uint)(4 * (Header.GetWidth() * ((yBlock * 4) + pY) + (xBlock * 4) + pX));
                                    destData[destIndex + 1] = fileData[dataOffset + 0]; //Green
                                    destData[destIndex + 0] = fileData[dataOffset + 1]; //Blue
                                    dataOffset += 2;
                                }
                            }

                        }
                    }

                    //Test? 
                    Bitmap bmp = new Bitmap((int)Header.GetWidth(), (int)Header.GetHeight());
                    Rectangle rect = new Rectangle(0, 0, (int)Header.GetWidth(), (int)Header.GetHeight());
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

                    //Unsafe mass copy, yay
                    IntPtr ptr = bmpData.Scan0;
                    Marshal.Copy(destData, 0, ptr, destData.Length);
                    bmp.UnlockBits(bmpData);

                    bmp.Save("poked_with_stick.png");
                }
                break;
                case ImageFormat.C4:
                {
                    //4 bpp, 8 block width/height, block size 32 bytes, possible palettes (IA8, RGB565, RGB5A3)
                    uint numBlocksW = Header.GetWidth() / 8; //8 block width
                    uint numBlocksH = Header.GetHeight() / 8; //8 block height 

                    int dataOffset = Header.ImageDataOffset;
                    byte[] destData = new byte[Header.GetWidth() * Header.GetHeight() * 8];

                    //Read the indexes from the file
                    for (int yBlock = 0; yBlock < numBlocksH; yBlock++)
                    {
                        for (int xBlock = 0; xBlock < numBlocksW; xBlock++)
                        {
                            //Inner Loop for pixels
                            for (int pY = 0; pY < 8; pY++)
                            {
                                for (int pX = 0; pX < 8; pX += 2)
                                {
                                    //Ensure we're not reading past the end of the image.
                                    if ((xBlock * 8 + pX >= Header.GetWidth()) || (yBlock * 8 + pY >= Header.GetHeight()))
                                        continue;

                                    byte t = (byte)(fileData[dataOffset] & 0xF0);
                                    byte t2 = (byte)(fileData[dataOffset] & 0x0F);

                                    destData[Header.GetWidth() * ((yBlock * 8) + pY) + (xBlock * 8) + pX + 0] = (byte)(t >> 4);
                                    destData[Header.GetWidth() * ((yBlock * 8) + pY) + (xBlock * 8) + pX + 1] = t2;

                                    dataOffset += 1;
                                }
                            }
                        }
                    }

                    //Now look them up in the palette and turn them into actual colors.
                    byte[] finalDest = new byte[destData.Length / 2];

                    int pixelSize = Header.GetPaletteFormat() == PaletteFormat.IA8 ? 2 : 4;
                    int destOffset = 0;
                    for (int y = 0; y < Header.GetHeight(); y++)
                    {
                        for (int x = 0; x < Header.GetWidth(); x++)
                        {
                            UnpackPixelFromPalette(destData[y * Header.GetWidth() + x], ref finalDest, destOffset,
                                ImagePalette.GetBytes(), Header.GetPaletteFormat());
                            destOffset += pixelSize;
                        }
                    }
                    //Test? 
                    Bitmap bmp = new Bitmap((int)Header.GetWidth(), (int)Header.GetHeight());
                    Rectangle rect = new Rectangle(0, 0, (int)Header.GetWidth(), (int)Header.GetHeight());
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

                    //Unsafe mass copy, yay
                    IntPtr ptr = bmpData.Scan0;
                    Marshal.Copy(finalDest, 0, ptr, finalDest.Length);
                    bmp.UnlockBits(bmpData);

                    bmp.Save("poked_with_stick2.png");

                    
                }
                break;
                case ImageFormat.RGB565:
                {
                    //16 bpp, 4 block width/height, block size 32 bytes, color.
                    uint numBlocksW = Header.GetWidth() / 4; //4 byte block width
                    uint numBlocksH = Header.GetHeight() / 4; //4 byte block height 

                    int dataOffset = Header.ImageDataOffset;
                    byte[] destData = new byte[Header.GetWidth() * Header.GetHeight() * 4];

                    //Read the indexes from the file
                    for (int yBlock = 0; yBlock < numBlocksH; yBlock++)
                    {
                        for (int xBlock = 0; xBlock < numBlocksW; xBlock++)
                        {
                            //Inner Loop for pixels
                            for (int pY = 0; pY < 4; pY++)
                            {
                                for (int pX = 0; pX < 4; pX++)
                                {
                                    //Ensure we're not reading past the end of the image.
                                    if ((xBlock * 4 + pX >= Header.GetWidth()) || (yBlock * 4 + pY >= Header.GetHeight()))
                                        continue;

                                    ushort sourcePixel = (ushort)FSHelpers.Read16(fileData, (int)dataOffset);
                                    RGB565ToRGBA8(sourcePixel, ref destData,
                                        (int) (4*(Header.GetWidth()*((yBlock*4) + pY) + (xBlock*4) + pX)));

                                    dataOffset += 2;
                                }
                            }
                        }
                    }

                    //Test? 
                    Bitmap bmp = new Bitmap((int)Header.GetWidth(), (int)Header.GetHeight());
                    Rectangle rect = new Rectangle(0, 0, (int)Header.GetWidth(), (int)Header.GetHeight());
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

                    //Unsafe mass copy, yay
                    IntPtr ptr = bmpData.Scan0;
                    Marshal.Copy(destData, 0, ptr, destData.Length);
                    bmp.UnlockBits(bmpData);

                    bmp.Save("poked_with_stick3.png");
                }
                break;   
                case ImageFormat.CMPR:
                {
                    //4 bpp, 8 block width/height, block size 32 bytes, color.
                    //Ahhh, a classic case of S3TC1 / DXT1 compression.
                    //uint numBlocksW = Header.GetWidth() / 4; //4 byte block width
                    //uint numBlocksH = Header.GetHeight() / 4; //4 byte block height 

                    int dataOffset = Header.ImageDataOffset;
                    byte[] destData = new byte[Header.GetWidth() * Header.GetHeight() * 4];

                    //Read the indexes from the file
                    /*for (int yBlock = 0; yBlock < Header.GetHeight(); yBlock += 2)
                    {
                        for (int xBlock = 0; xBlock < Header.GetWidth(); xBlock += 2)
                        {
                            //Inner Loop for pixels
                            for (int pY = 0; pY < 2; pY++)
                            {
                                for (int pX = 0; pX < 2; pX++)
                                {
                                    //Ensure we're not reading past the end of the image.
                                    if (4 * (xBlock + pX) >= Header.GetWidth() && 4 *(yBlock + pY) >= Header.GetHeight())
                                        continue;

                                    Buffer.BlockCopy(fileData, (int)dataOffset, destData,
                                        (int)(8*((yBlock + pY)*Header.GetWidth()/4 + xBlock + pX)), 8);

                                    dataOffset += 8;
                                }
                            }
                        }
                    }

                    for (uint i = 0; i < Header.GetWidth()*Header.GetHeight()/2; i += 8)
                    {
                        //Byte order flip some data
                        byte temp = destData[i + 1];
                        destData[i + 1] = destData[i];
                        destData[i] = temp;

                        temp = destData[i + 3];
                        destData[i + 3] = destData[i + 2];
                        destData[i + 2] = temp;

                        destData[i + 4] = S3TC1ReverseByte(destData[i + 4]);
                        destData[i + 5] = S3TC1ReverseByte(destData[i + 5]);
                        destData[i + 6] = S3TC1ReverseByte(destData[i + 6]);
                        destData[i + 7] = S3TC1ReverseByte(destData[i + 7]);
                    }*/

                    FixS3TC1(ref destData, fileData, (int)dataOffset, (int) Header.GetWidth(), (int)Header.GetHeight());

                    byte[] finalData = new byte[(Header.GetWidth()*Header.GetHeight())*4];
                    DecompressDXT1(ref finalData, destData, (int)Header.GetWidth(), (int)Header.GetHeight());

                    //Test? 
                    Bitmap bmp = new Bitmap((int)Header.GetWidth(), (int)Header.GetHeight());
                    Rectangle rect = new Rectangle(0, 0, (int)Header.GetWidth(), (int)Header.GetHeight());
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

                    //Unsafe mass copy, yay
                    IntPtr ptr = bmpData.Scan0;
                    Marshal.Copy(finalData, 0, ptr, finalData.Length);
                    bmp.UnlockBits(bmpData);

                    bmp.Save("poked_with_stick4.png");
                }
                break;
                default:
                    Console.WriteLine("Unsupported image format {0}!", Header.GetFormat());
                    break;
            }
        }

        private byte S3TC1ReverseByte(byte b)
        {
            byte b1 = (byte) (b & 0x3);
            byte b2 = (byte) (b & 0xC);
            byte b3 = (byte) (b & 0x30);
            byte b4 = (byte) (b & 0xC0);

            return (byte) ((b1 << 6) | (b2 << 2) | (b3 >> 2) | (b4 >> 6));
        }

        void FixS3TC1(ref byte[] Dest, byte[] Src, int S, int Width, int Height)
        {
            int y, x, dy, dx;
            for (y = 0; y < Height / 4; y += 2)
                for (x = 0; x < Width / 4; x += 2)
                    for (dy = 0; dy < 2; ++dy)
                        for (dx = 0; dx < 2; ++dx, S += 8)
                            if (4 * (x + dx) < Width && 4 * (y + dy) < Height)
                                Buffer.BlockCopy(Src, S, Dest, 8 * ((y + dy) * Width / 4 + x + dx), 8);

            for (int i = 0; i < Width * Height / 2; i += 8)
            {
                FSHelpers.Swap(ref Dest[i], ref Dest[i + 1]);
                FSHelpers.Swap(ref Dest[i + 2], ref Dest[i + 3]);

                Dest[i + 4] = S3TC1ReverseByte(Dest[i + 4]);
                Dest[i + 5] = S3TC1ReverseByte(Dest[i + 5]);
                Dest[i + 6] = S3TC1ReverseByte(Dest[i + 6]);
                Dest[i + 7] = S3TC1ReverseByte(Dest[i + 7]);
            }
        }

        private void DecompressDXT1(ref byte[] Dest, byte[] Src, int Width, int Height)
        {
            uint Address = 0;

            int y = 0, x = 0, iy = 0, ix = 0;
            for (y = 0; y < Height; y += 4)
            {
                for (x = 0; x < Width; x += 4)
                {
                    UInt16 Color1 = FSHelpers.Read16Swap(Src, Address);
                    UInt16 Color2 = FSHelpers.Read16Swap(Src, Address + 2);
                    UInt32 Bits = FSHelpers.Read32Swap(Src, Address + 4);
                    Address += 8;

                    byte[][] ColorTable = new byte[4][];
                    for (int i = 0; i < 4; i++)
                        ColorTable[i] = new byte[4];

                    RGB565ToRGBA8(Color1, ref ColorTable[0], 0);
                    RGB565ToRGBA8(Color2, ref ColorTable[1], 0);

                    if (Color1 > Color2)
                    {
                        ColorTable[2][0] = (byte)((2 * ColorTable[0][0] + ColorTable[1][0] + 1) / 3);
                        ColorTable[2][1] = (byte)((2 * ColorTable[0][1] + ColorTable[1][1] + 1) / 3);
                        ColorTable[2][2] = (byte)((2 * ColorTable[0][2] + ColorTable[1][2] + 1) / 3);
                        ColorTable[2][3] = 0xFF;

                        ColorTable[3][0] = (byte)((ColorTable[0][0] + 2 * ColorTable[1][0] + 1) / 3);
                        ColorTable[3][1] = (byte)((ColorTable[0][1] + 2 * ColorTable[1][1] + 1) / 3);
                        ColorTable[3][2] = (byte)((ColorTable[0][2] + 2 * ColorTable[1][2] + 1) / 3);
                        ColorTable[3][3] = 0xFF;
                    }
                    else
                    {
                        ColorTable[2][0] = (byte)((ColorTable[0][0] + ColorTable[1][0] + 1) / 2);
                        ColorTable[2][1] = (byte)((ColorTable[0][1] + ColorTable[1][1] + 1) / 2);
                        ColorTable[2][2] = (byte)((ColorTable[0][2] + ColorTable[1][2] + 1) / 2);
                        ColorTable[2][3] = 0xFF;

                        ColorTable[3][0] = (byte)((ColorTable[0][0] + 2 * ColorTable[1][0] + 1) / 3);
                        ColorTable[3][1] = (byte)((ColorTable[0][1] + 2 * ColorTable[1][1] + 1) / 3);
                        ColorTable[3][2] = (byte)((ColorTable[0][2] + 2 * ColorTable[1][2] + 1) / 3);
                        ColorTable[3][3] = 0x00;
                    }

                    for (iy = 0; iy < 4; ++iy)
                    {
                        for (ix = 0; ix < 4; ++ix)
                        {
                            if (((x + ix) < Width) && ((y + iy) < Height))
                            {
                                int di = 4 * ((y + iy) * Width + x + ix);
                                int si = (int)(Bits & 0x3);
                                Dest[di + 0] = ColorTable[si][0];
                                Dest[di + 1] = ColorTable[si][1];
                                Dest[di + 2] = ColorTable[si][2];
                                Dest[di + 3] = ColorTable[si][3];
                            }
                            Bits >>= 2;
                        }
                    }
                }
            }
        }

        public uint GetFileSize()
        {
            double bpp;
            switch (Header.GetFormat())
            {
                case ImageFormat.CMPR:
                case ImageFormat.C4:
                case ImageFormat.I4: bpp = 4; break;
                case ImageFormat.I8:
                case ImageFormat.C8:
                case ImageFormat.IA4: bpp = 8; break;
                case ImageFormat.C14X2:
                case ImageFormat.IA8:
                case ImageFormat.RGB565:
                case ImageFormat.RGB5A3: bpp = 16; break;
                case ImageFormat.RGBA32: bpp = 32; break;
                default:
                    Console.WriteLine("Unknown Header Format for GetFileSize. Assuming 16bpp!");
                    bpp = 16;
                    break;
            }

            return (uint) (FileHeader.Size + (Header.GetWidth()*Header.GetHeight()*(bpp/8)) + (Header.PaletteEntryCount*2));
        }

        private void UnpackPixelFromPalette(int paletteIndex, ref byte[] dest, int offset, byte[] paletteData, PaletteFormat format)
        {
            switch (format)
            {
                case PaletteFormat.IA8:
                    dest[0] = paletteData[2 * paletteIndex + 1];
                    dest[1] = paletteData[2 * paletteIndex + 0];
                    break;
                case PaletteFormat.RGB565:
                    RGB565ToRGBA8((ushort)FSHelpers.Read16(paletteData, 2 * paletteIndex), ref dest, offset);
                    break;
                case PaletteFormat.RGB5A3:
                    RGB5A3ToRGBA8((ushort)FSHelpers.Read16(paletteData, 2 * paletteIndex), ref dest, offset);
                    break;
            }
        }

        /// <summary>
        /// Convert a RGB565 encoded pixel (two bytes in length) to a RGBA (4 byte in length)
        /// pixel.
        /// </summary>
        /// <param name="sourcePixel">RGB565 encoded pixel.</param>
        /// <param name="dest">Destination array for RGBA pixel.</param>
        /// <param name="destOffset">Offset into destination array to write RGBA pixel.</param>
        private void RGB565ToRGBA8(ushort sourcePixel, ref byte[] dest, int destOffset)
        {
            byte r, g, b;
            r = (byte)((sourcePixel & 0xF100) >> 11);
            g = (byte)((sourcePixel & 0x7E0) >> 5);
            b = (byte)((sourcePixel & 0x1F));

            r = (byte)((r << (8 - 5)) | (r >> (10 - 8)));
            g = (byte)((g << (8 - 6)) | (g >> (12 - 8)));
            b = (byte)((b << (8 - 5)) | (b >> (10 - 8)));

            dest[destOffset] = b;
            dest[destOffset + 1] = g;
            dest[destOffset + 2] = r;
            dest[destOffset + 3] = 0xFF; //Set alpha to 1
        }

        /// <summary>
        /// Convert a RGB5A3 encoded pixel (two bytes in length) to an RGBA (4 byte in length)
        /// pixel.
        /// </summary>
        /// <param name="sourcePixel">RGB5A3 encoded pixel.</param>
        /// <param name="dest">Destination array for RGBA pixel.</param>
        /// <param name="destOffset">Offset into destination array to write RGBA pixel.</param>
        private void RGB5A3ToRGBA8(ushort sourcePixel, ref byte[] dest, int destOffset)
        {
            byte r, g, b, a;

            //No alpha bits
            if ((sourcePixel & 0x8000) == 0x8000)
            {
                a = 0xFF;
                r = (byte)((sourcePixel & 0x7C00) >> 10);
                g = (byte)((sourcePixel & 0x3E0) >> 5);
                b = (byte)(sourcePixel & 0x1F);

                r = (byte)((r << (8 - 5)) | (r >> (10 - 8)));
                g = (byte)((g << (8 - 5)) | (g >> (10 - 8)));
                b = (byte)((b << (8 - 5)) | (b >> (10 - 8)));
            }
            //Alpha bits
            else
            {
                a = (byte)((sourcePixel & 0x7000) >> 12);
                r = (byte)((sourcePixel & 0xF00) >> 8);
                g = (byte)((sourcePixel & 0xF0) >> 4);
                b = (byte)(sourcePixel & 0xF);

                a = (byte)((a << (8 - 3)) | (a << (8 - 6)) | (a >> (9 - 8)));
                r = (byte)((r << (8 - 4)) | r);
                g = (byte)((g << (8 - 4)) | g);
                b = (byte)((b << (8 - 4)) | b);
            }

            dest[destOffset + 0] = a;
            dest[destOffset + 1] = b;
            dest[destOffset + 2] = g;
            dest[destOffset + 3] = r;
        }

        //Temp for saving
        private byte[] _dataCache;


        public override void Save(BinaryWriter stream)
        {
            FSHelpers.WriteArray(stream, _dataCache);
        }
    }
}