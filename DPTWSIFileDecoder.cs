using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
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
        /// <param name="width">实际要取得的图像宽度</param>
        /// <param name="height">实际要取得的图像高度</param>
        /// <param name="layer"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public byte[] ReadRegion(int x, int y, int width, int height, int layer)
        { 
            if (layer<0 || layer > 2)
            {
                throw new ArgumentException("Layer 只能为0,1,2层");
            }
            double scale = 1 / Math.Pow(4, layer);

            // 按照缩放比例将x,y 转换为当前层的坐标
            int scaledX = (int)(x*scale);
            int scaledY = (int)(y*scale);

            int scaledWSIWidth = (int)(DptFile.Width * scale);
            int scaledWSIHeight = (int)(DptFile.Height * scale);

            // 计算图像外围内的实际终点
            int realRegionStartX = Math.Max(0, scaledX);
            int realRegionStartY = Math.Max(0, scaledY);
            int realRegionEndX = Math.Min(scaledWSIWidth - 1, scaledX + width - 1);
            int realRegionEndY = Math.Min(scaledWSIHeight - 1, scaledY + height - 1);

            // 当前尺度下单张图的实际大小
            int tileSizeX = (int)((DptFile.SingleImageWidth - DptFile.Overlap) * scale);
            int tileSizeY = (int)((DptFile.SingleImageHeight - DptFile.Overlap) * scale);

            List<ImagePosInfo> posInfos = new List<ImagePosInfo>();
            // 计算起终点所处的小图序号
            int left = realRegionStartX / tileSizeX;
            int right = realRegionEndX / tileSizeX;
            int top = realRegionStartY / tileSizeY;
            int bottom = realRegionEndY / tileSizeY;

            // 创建多线程读取PosInfoList
            Parallel.For(left, right+1, imageX =>{
                for (int imageY = top; imageY < bottom + 1; imageY++)
                {
                    var posInfo = new ImagePosInfo()
                    {
                        Layer = (sbyte)layer,
                        X = (uint)(imageX * tileSizeX / scale),
                        Y = (uint)(imageY * tileSizeY / scale),
                        Z = 0
                    };
                    posInfos.Add(posInfo);
                }
            });
            

            ConcurrentDictionary<ImagePosInfo, Mat> tiles = new ConcurrentDictionary<ImagePosInfo,Mat>();

            Parallel.ForEach(posInfos, (posInfo) =>
            {
                try
                {
                    ImageInfos.TryGetValue(posInfo, out var dataInfo);
                    // TODO: 多线程读取jpeg数据
                    Stream readStream = new FileStream(DptFile.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    Mat tile = ReadSingleImageData(posInfo, readStream);
                    // TODO: 同时多线程解码jpeg数据
                    tiles[posInfo] = tile;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    tiles[posInfo] = new Mat();
                }
            });

            // 将区域融合为一个Mat
            using Mat regionMat = new Mat(new Size(realRegionEndX - x + 1, realRegionEndY -y +1),MatType.CV_8UC3);
            using Mat failedImgMat = new Mat(new Size((int)(DptFile.SingleImageWidth*scale),(int)(DptFile.SingleImageHeight*scale)),
                                           MatType.CV_8UC3, new Scalar(251,251,251));
            int idx = 0; //序号：用于保存
            foreach (var jpegImg in tiles)
            {
                // 读取jpeg数据
                using Mat singleImgMat = jpegImg.Value.Size() == new Size(0,0) ? failedImgMat : jpegImg.Value.Resize(failedImgMat.Size());
                Cv2.Resize(singleImgMat, singleImgMat, new Size(DptFile.SingleImageWidth, DptFile.SingleImageHeight));
                Cv2.CvtColor(singleImgMat, singleImgMat, ColorConversionCodes.RGB2BGR);

                // 测试: 保存所有tile
                singleImgMat.SaveImage($"D:/yuxx/dpt_write_test/{jpegImg.Key.X}-{jpegImg.Key.Y}.jpg");


                int imgX = (int)(jpegImg.Key.X * scale);
                int imgY = (int)(jpegImg.Key.Y * scale);
                int imgWidth = (int)(DptFile.SingleImageWidth * scale);
                int imgHeight = (int)(DptFile.SingleImageHeight * scale);

                //singleImgMat.SaveImage("D:/yuxx/test_dpt_read.jpg");

                // 处理截取
                int startX = Math.Max(x,imgX);
                int startY = Math.Max(y,imgY);
                int endX = Math.Min(realRegionEndX, imgX + imgWidth);
                int endY = Math.Min(realRegionEndY, imgY + imgHeight);

                using Mat roi = singleImgMat.SubMat(startY - imgY, endY - imgY, startX - imgX, endX - imgX);
                using Mat subRegion = regionMat.SubMat(startY - y, endY - y, startX - x, endX - x);
                roi.CopyTo(subRegion);

                //regionMat.SaveImage("D:/yuxx/test_dpt_read2.jpg");
                idx++;
            }

            // TODO: 过大数组会导致内存溢出,需要优化
            regionMat.SaveImage("D:/yuxx/test_dpt_read.jpg");
            byte[] result = new byte[regionMat.Width * regionMat.Height * 3];
            Marshal.Copy(regionMat.Data, result, 0, result.Length);
            return result;
        }

        public byte[] GetTile(int row, int col, int tileSize, int layer){
            int btmLayerPosX = row*tileSize * (int)Math.Pow(4,layer);
            int btmLayerPosY = col*tileSize * (int)Math.Pow(4,layer);
            byte[] tileBytes = ReadRegion(btmLayerPosX, btmLayerPosY, tileSize, tileSize, layer);
            return tileBytes;
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
        public Mat ReadSingleImageData(ImagePosInfo posInfo, Stream readStream)
        {
            using (readStream)
            using (BinaryReader reader = new BinaryReader(readStream))
            {
                ImageDataInfo dataInfo = ImageInfos[posInfo];
                readStream.Seek(dataInfo.Offset, SeekOrigin.Begin);
                byte[] imageData = reader.ReadBytes(dataInfo.Length);
                if (imageData[0] != 0XFF || imageData[1] != 0XD8)
                    throw new Exception($"The Magic Number is {imageData[0]} {imageData[1]}, Not jpeg data ");
                if (imageData == null)
                    throw new Exception($"This Offset contains no data");
                Mat jpegMat = Cv2.ImDecode(imageData, ImreadModes.Color);
                return jpegMat;
            }   
        }
    }
}
