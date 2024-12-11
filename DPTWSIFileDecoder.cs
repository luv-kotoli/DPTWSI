using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace DPTWSITest
{
    public class DPTWSIFileDecoder
    {
        private DPTWSIFile DptFile;
        private Stream Stream;
        private BinaryReader Reader;
        private ConcurrentDictionary<ImagePosInfo, ImageDataInfo> ImageInfos = new ConcurrentDictionary<ImagePosInfo, ImageDataInfo>();

        public DPTWSIFileDecoder(string filePath)
        {
            DptFile = new DPTWSIFile(filePath);
            Stream = DptFile.GetFileStream();
            Reader = DptFile.GetReader();
            int imageNum = (int)DptFile.ImageNum;

            // 初始化ImageInfos对象
            // 将ImageInfo部分读入内存
            DptFile.GetFileStream().Seek(DPTWSIFileConsts.ImageInfoStartOffset, SeekOrigin.Begin);
            byte[] imageInfoBytes = Reader.ReadBytes(imageNum*DPTWSIFileConsts.ImageInfoSize);
            Parallel.For(0, imageNum, i =>
            {
                int startOffset = i * DPTWSIFileConsts.ImageInfoSize;
                ImagePosInfo posInfo = new ImagePosInfo()
                {
                    Layer = (sbyte)imageInfoBytes[startOffset],
                    X = BitConverter.ToUInt32(imageInfoBytes,startOffset+1),
                    Y = BitConverter.ToUInt32(imageInfoBytes, startOffset+5),
                    Z = imageInfoBytes[startOffset+9],
                };
                ImageDataInfo dataInfo = new ImageDataInfo()
                {
                    Length = BitConverter.ToInt32(imageInfoBytes, startOffset + 10),
                    Offset = BitConverter.ToInt64(imageInfoBytes, startOffset + 14),
                };
                ImageInfos.TryAdd(posInfo, dataInfo);
            });
        }

        /// <summary>
        /// 用于读取图像中一定区域内的数据,暂时设定Z固定为0
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="layer"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public byte[] ReadRegion(int x, int y, int width, int height, int layer)
        {
            
            if (layer<0 || layer > 2)
            {
                throw new ArgumentException("Layer 只能为0,1,2层");
            }

            ConcurrentBag<ImagePosInfo> posInfos = new ConcurrentBag<ImagePosInfo>();
            // 计算起终点所处的小图序号
            int left = x / (int)(DptFile.SingleImageWidth - DptFile.Overlap);
            int right = Math.Min(x + width, (int)DptFile.Width) / (int)(DptFile.SingleImageWidth - DptFile.Overlap);
            int top = y / (int)(DptFile.SingleImageHeight - DptFile.Overlap);
            int bottom = Math.Min(y + height, (int)DptFile.Height) / (int)(DptFile.SingleImageHeight - DptFile.Overlap);

            // 计算起终点小图中的起始像素
            int startLeftPixel = x % (int)(DptFile.SingleImageWidth - DptFile.Overlap);
            int endRightPixel = Math.Min(x + width, (int)DptFile.Width) % (int)(DptFile.SingleImageWidth - DptFile.Overlap);
            int startTopPixel = y % (int)(DptFile.SingleImageHeight - DptFile.Overlap);
            int endBottomPixel = Math.Min(y + height, (int)DptFile.Height) % (int)(DptFile.SingleImageHeight - DptFile.Overlap);

            // 创建多线程读取PosInfoList
            for (int imageX = left; imageX < right + 1; imageX++)
            {
                for (int imageY = top; imageY < bottom + 1; imageY++)
                {
                    var posInfo = new ImagePosInfo()
                    {
                        Layer = (sbyte)layer,
                        X = (uint)x,
                        Y = (uint)y,
                        Z = 0
                    };
                    posInfos.Add(posInfo);
                }
            }

            Dictionary<ImagePosInfo, byte[]> jpegImgs = new Dictionary<ImagePosInfo,byte[]>();

            foreach (ImagePosInfo posInfo in posInfos)
            {
                var dataInfo = ImageInfos[posInfo];
                Stream.Seek(dataInfo.Offset, SeekOrigin.Begin);
                byte[] jpegImg = ReadSingleImageData(posInfo);
                jpegImgs[posInfo] = jpegImg;
            }

            // 将区域融合为一个Mat
            int realRegionEndX = Math.Min((int)DptFile.Width - 1, x + width - 1);
            int realRegionEndY = Math.Min((int)DptFile.Height - 1, y + height - 1);
            

            Mat regionMat = new Mat(new Size(realRegionEndX - x + 1, realRegionEndY -y +1),MatType.CV_8UC3);
            foreach (var jpegImg in jpegImgs)
            {
                Mat singleImgMat = Mat.FromImageData(jpegImg.Value, ImreadModes.Color);
                Cv2.Resize(singleImgMat, singleImgMat, new Size(DptFile.SingleImageWidth, DptFile.SingleImageHeight));
                // 判断图像的位置
                if (jpegImg.Key.X == left)
                {
                    int startX = startLeftPixel;
                }



                
            }

            return new byte[0];
        }

        public int GetBestDownsampleLayer()
        {
            return 0;
        }

        /// <summary>
        /// 读取单张图像的jpeg二进制数据
        /// </summary>
        /// <param name="posInfo"></param>
        /// <returns></returns>
        public byte[] ReadSingleImageData(ImagePosInfo posInfo)
        {
            ImageDataInfo dataInfo = ImageInfos[posInfo];
            Stream.Seek(dataInfo.Offset, SeekOrigin.Begin);
            byte[] imageData = Reader.ReadBytes(dataInfo.Length);
            if (imageData[0] != 0XFF || imageData[1] != 0XD8)
                throw new Exception($"The Magic Number is {imageData[0]} {imageData[1]}, Not jpeg data ");
            if (imageData == null)
                throw new Exception($"This Offset contains no data");
            return imageData;
        }
    }
}
