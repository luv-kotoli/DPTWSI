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
        public DPTWSIFile DptFile;
        private Stream Stream;
        private BinaryReader Reader;
        private ConcurrentDictionary<ImagePosInfo, ImageDataInfo> ImageInfos = new ConcurrentDictionary<ImagePosInfo, ImageDataInfo>();
        public int WSIWidth;
        public int WSIHeight;

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
            ConcurrentBag<int> xPosList = new ConcurrentBag<int>(); // 用来记录实际的全场图宽度
            ConcurrentBag<int> yPosList = new ConcurrentBag<int>(); // 用来记录实际的全场图宽度

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
                xPosList.Add(BitConverter.ToInt32(imageInfoBytes, startOffset + 1));
                yPosList.Add(BitConverter.ToInt32(imageInfoBytes, startOffset + 5));
                ImageDataInfo dataInfo = new ImageDataInfo()
                {
                    Length = BitConverter.ToInt32(imageInfoBytes, startOffset + 10),
                    Offset = BitConverter.ToInt64(imageInfoBytes, startOffset + 14),
                };
                ImageInfos.TryAdd(posInfo, dataInfo);
            });

            WSIWidth = xPosList.Max()+DptFile.SingleImageWidth;
            WSIHeight = yPosList.Max() + DptFile.SingleImageHeight;
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
        public byte[] ReadRegion(int x, int y, int width, int height, int layer, out int realWidth, out int realHeight, int z = 0)
        { 
            if (layer<0 || layer > 2)
            {
                throw new ArgumentException("Layer 只能为0,1,2层");
            }

            if (z > DptFile.ZStacks)
            {
                throw new ArgumentException($"非法访问:当前文件只扫描了{DptFile.ZStacks}层，正在访问第{z}层");
            }
            double scale = 1 / Math.Pow(4, layer);

            // 按照缩放比例将x,y 转换为当前层的坐标
            int scaledX = (int)(x*scale);
            int scaledY = (int)(y*scale);

            // 缩放后的全场图宽度
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

            ConcurrentBag<ImagePosInfo> posInfos = new ConcurrentBag<ImagePosInfo>();
            // 计算起终点所处的小图序号
            int left = realRegionStartX / tileSizeX;
            int right = realRegionEndX / tileSizeX;
            int top = realRegionStartY / tileSizeY;
            int bottom = realRegionEndY / tileSizeY;

            // 创建多线程读取PosInfoList
            Parallel.For(left, right+1, imageX =>{
                for (int imageY = top; imageY < bottom + 1; imageY++)
                {
                    // tileX,Y 用来表示读取的tile的像素位置
                    uint tileX = (uint)(imageX * tileSizeX / scale);
                    uint tileY = (uint)(imageY * tileSizeY / scale);
                    if (tileX + DptFile.SingleImageWidth > WSIWidth || tileY + DptFile.SingleImageHeight > WSIHeight) continue;
                    var posInfo = new ImagePosInfo()
                    {
                        Layer = (sbyte)layer,
                        X = tileX,
                        Y = tileY,
                        Z = (byte)z,
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

                    //if (!Directory.Exists($"D:/yuxx/dpt_write_test/layer{posInfo.Layer}")){
                    //    Directory.CreateDirectory($"D:/yuxx/dpt_write_test/layer{posInfo.Layer}");
                    //}
                    //tile.SaveImage($"D:/yuxx/dpt_write_test/layer{posInfo.Layer}/{posInfo.X}-{posInfo.Y}.jpg");

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
            using Mat regionMat = new Mat(new Size(realRegionEndX - scaledX + 1, realRegionEndY - scaledY +1),MatType.CV_8UC3,new Scalar(0,0,0));
            using Mat failedImgMat = new Mat(new Size((int)(DptFile.SingleImageWidth*scale),(int)(DptFile.SingleImageHeight*scale)),
                                           MatType.CV_8UC3, new Scalar(251,251,251));
            int idx = 0; //序号：用于保存
            foreach (var jpegImg in tiles)
            {
                // 读取jpeg数据
                List<FusionDirection> directions = CalculateTileFusionDirection(jpegImg.Key);
                using Mat singleImgMat = jpegImg.Value.Size() == new Size(0,0) ? failedImgMat : jpegImg.Value.Resize(failedImgMat.Size());
                Cv2.Resize(singleImgMat, singleImgMat, new Size(DptFile.SingleImageWidth*scale, DptFile.SingleImageHeight*scale));
                Cv2.CvtColor(singleImgMat, singleImgMat, ColorConversionCodes.RGB2BGR);

                Console.WriteLine(string.Join('-', directions));
                DPTWSIUtils.WeightedFusionSingle(singleImgMat, (int)DptFile.Overlap, directions);

                // 测试: 保存所有tile
                //singleImgMat.SaveImage($"D:/yuxx/dpt_write_test/{jpegImg.Key.X}-{jpegImg.Key.Y}.jpg");

                int imgX = (int)(jpegImg.Key.X * scale);
                int imgY = (int)(jpegImg.Key.Y * scale);
                int imgWidth = (int)(DptFile.SingleImageWidth * scale);
                int imgHeight = (int)(DptFile.SingleImageHeight * scale);

                //singleImgMat.SaveImage("D:/yuxx/test_dpt_read.jpg");

                // 处理截取 scaledX，Y: 所取region起始位置, imgX,Y: 当前patch 左上角位置, realRegionEndX,Y: 所取region终点位置
                int startX = Math.Max(scaledX,imgX);
                int startY = Math.Max(scaledY,imgY);
                int endX = Math.Min(realRegionEndX, imgX + imgWidth-1); 
                int endY = Math.Min(realRegionEndY, imgY + imgHeight-1);
                Console.WriteLine($"regionMat Size:{regionMat.Size()}, End Position:{endX - scaledX}-{endY - scaledY}");
                using Mat roi = singleImgMat.SubMat(startY - imgY, endY - imgY, startX - imgX, endX - imgX);
                using Mat subRegion = regionMat.SubMat(startY - scaledY, endY - scaledY, startX - scaledX, endX - scaledX);
                //roi.CopyTo(subRegion);
                Cv2.Add(roi, subRegion, subRegion);

                //regionMat.SaveImage("D:/yuxx/test_dpt_read2.jpg");
                idx++;
            }

            // TODO: 过大数组会导致内存溢出,需要优化
            regionMat.SaveImage("D:/yuxx/test_dpt_read.jpg");
            byte[] result = new byte[regionMat.Width * regionMat.Height * 3];
            Marshal.Copy(regionMat.Data, result, 0, result.Length);
            realWidth = regionMat.Width;
            realHeight = regionMat.Height;
            return result;
        }

        public byte[] GetTile(int row, int col, int tileSize, int layer, out int realWidth, out int realHeight){
            int btmLayerPosX = row*tileSize * (int)Math.Pow(4,layer);
            int btmLayerPosY = col*tileSize * (int)Math.Pow(4,layer);
            
            byte[] tileBytes = ReadRegion(btmLayerPosX, btmLayerPosY, tileSize, tileSize, layer, out realWidth, out realHeight);
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

        public List<FusionDirection> CalculateTileFusionDirection(ImagePosInfo info)
        {
            List<FusionDirection> fusionDirections = new List<FusionDirection>();
            if (info.X != 0) fusionDirections.Add(FusionDirection.Left);
            if (info.Y != 0) fusionDirections.Add(FusionDirection.Top);
            if (info.X + DptFile.SingleImageWidth != WSIWidth) fusionDirections.Add(FusionDirection.Right);
            if (info.Y + DptFile.SingleImageHeight != WSIHeight) fusionDirections.Add(FusionDirection.Bottom);

            return fusionDirections;
        }
    }
}
