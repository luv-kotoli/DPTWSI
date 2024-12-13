using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DPTWSITest
{
    public class DPTWSIFileConsts
    {
        public const string MagicNumber = "DPTWSI";
        public const int HeaderSize = 32;
        public const int ImageInfoSize = 22;
        public const int ImageInfoStartOffset = HeaderSize;
        public const int DataStartOffset = 6187500 + HeaderSize;
    }
    public class ImagePosInfo : IEquatable<ImagePosInfo>
    {
        public sbyte Layer { get; set; }
        public uint X { get; set; }
        public uint Y { get; set; }
        public byte Z { get; set; }

        public override string ToString()
        {
            return $"{X}-{Y}-{Z}-{Layer}";
        }

        public bool Equals(ImagePosInfo other)
        {
            if (other == null)
            {
                return false;
            }
            return Layer == other.Layer && X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            return Equals((ImagePosInfo)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Layer.GetHashCode();
                hash = hash * 23 + X.GetHashCode();
                hash = hash * 23 + Y.GetHashCode();
                hash = hash * 23 + Z.GetHashCode();
                return hash;
            }
        }
    }

    public class ImageDataInfo
    {
        public int Length { get; set; } = 0;
        public long Offset { get; set; } = 0;

        public override string ToString()
        {
            return $"{Length}-{Offset}";
        }
    }
    public class DPTWSIFile
    {
        // 文件属性
        private bool IsOpen = false;
        private bool IsWrite = false;

        // 图像属性
        //public short Cols { set; get; } // X方向图像数量 
        //public short Rows { set; get; } // Y方向图像数量
        public uint ImageNum { set; get; }
        public sbyte ZStacks { set; get; }  // Z轴扫描层数
        public short SingleImageWidth { set; get; }  // 单张图像宽度
        public short SingleImageHeight { set; get; } // 单张头像高度
        public uint Width { set; get; }  // WSI 总宽度
        public uint Height { set; get; } // WSI 总高度
        public float Mpp { set; get; }   // WSI 分辨率 (厘米)
        public uint Overlap { set; get; } // 单张间重叠像素数 

        public string FileName { set; get; }
        private Stream Stream { get; set; }
        private BinaryReader? Reader { get; set; }
        private BinaryWriter? Writer { get; set; } 

        public DPTWSIFile(string filePath) // 用于打开一个DPT文件
        {
            FileName = filePath;
            Stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,FileShare.Read);
            Reader = new BinaryReader(Stream);
            {
                // Read and validate header
                string magic = new string(Reader.ReadChars(6));
                if (magic != DPTWSIFileConsts.MagicNumber)
                    throw new InvalidDataException("Invalid WSI file.");

                ImageNum = Reader.ReadUInt32();
                ZStacks = Reader.ReadSByte();
                SingleImageWidth = Reader.ReadInt16();
                SingleImageHeight = Reader.ReadInt16();
                Reader.ReadBytes(1); // skip blank byte
                Width = Reader.ReadUInt32();
                Height = Reader.ReadUInt32();
                Mpp = Reader.ReadSingle();
                Overlap = Reader.ReadUInt32();
            }
            IsOpen = true;
            IsWrite = false;
        }

        /// <summary>
        /// 用于写入一个DPT文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <param name="zstacks"></param>
        /// <param name="imageWidth"></param>
        /// <param name="imageHeight"></param>
        /// <param name="wsiWidth"></param>
        /// <param name="wsiHeight"></param>
        /// <param name="mpp"></param>
        /// <param name="overlap"></param>
        public DPTWSIFile(string filePath, uint imageNum, sbyte zstacks,
                          short imageWidth, short imageHeight,
                          uint wsiWidth, uint wsiHeight,
                          float mpp, uint overlap)
        {
            ImageNum = imageNum;
            ZStacks = zstacks;
            SingleImageWidth = imageWidth;
            SingleImageHeight = imageHeight;
            Width = wsiWidth;
            Height = wsiHeight;
            Mpp = mpp;
            Overlap = overlap;

            FileName = filePath;
            Stream = new FileStream(FileName, FileMode.Create, FileAccess.Write);
            Writer = new BinaryWriter(Stream);
            IsOpen = true;
            IsWrite = true;
        }

        public Stream GetFileStream()
        {
            return Stream;
        }

        public BinaryWriter GetWriter()
        {
            if (IsWrite && Writer != null) return Writer;
            else throw new Exception(" Now this file is in read mode");
        }

        public BinaryReader GetReader()
        {
            if (!IsWrite && Reader != null) return Reader;
            else throw new Exception(" Now this file is in write mode");
        }

        public void Close()
        {
            Stream.Close();
            if (IsWrite) Writer?.Close();
            else Reader?.Close();
            IsOpen = false;
        }

        public void ChangeName(string fileName)
        {
            if (IsOpen)
            {
                throw new IOException("该DPT文件正在被占用");
            }
            else
            {
                var tmpFileName = FileName;
                FileName = fileName;
                File.Move(tmpFileName, FileName);
            }
        }
    }
}
