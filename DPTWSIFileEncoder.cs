using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DPTWSITest
{
    public class DPTWSIFileEncoder
    {
        // 文件写入相关
        private DPTWSIFile DptFile;
        private Stream Stream;
        private BinaryWriter Writer;
        private long CurrOffset;
        private ConcurrentDictionary<ImagePosInfo, ImageDataInfo> ImageInfos = new ConcurrentDictionary<ImagePosInfo, ImageDataInfo>();

        public DPTWSIFileEncoder(uint imageNum, sbyte zStacks, short imageWidth, short imageHeight, uint WSIWidth, uint WSIHeight, float mpp, uint overlap)
        {


            string fileDir = Path.Combine("D:/yuxx/dpt_write_test/");
            string fileName = Path.Combine(fileDir, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds().ToString() + ".tmp");
            DptFile = new DPTWSIFile(fileName, imageNum, zStacks,
                                     imageWidth, imageHeight,
                                     WSIWidth, WSIHeight,
                                     mpp, overlap);
            Stream = DptFile.GetFileStream();
            Writer = DptFile.GetWriter();

            // write file head
            Writer.Write(DPTWSIFileConsts.MagicNumber.ToCharArray());
            Writer.Write(imageNum*3);
            Writer.Write(zStacks);
            Writer.Write(imageWidth);
            Writer.Write(imageHeight);
            Writer.Write(new byte());
            Writer.Write(WSIWidth);
            Writer.Write(WSIHeight);
            Writer.Write(mpp);
            Writer.Write(overlap);
            Writer.Flush(); // 写入到文件

            CurrOffset = DPTWSIFileConsts.DataStartOffset;
        }

        public void WriteImage(sbyte Layer, uint X, uint Y, byte Z, byte[] ImageData)
        {
            // jpeg 压缩图像, 并写入图像文件
            //byte[] jpegData = Image.ImEncode(".jpg");
            int length = ImageData.Length;
            // jump to image data write position
            Stream.Seek(CurrOffset, SeekOrigin.Begin);
            Writer.Write(ImageData);


            // 创建ImageInfo对象
            ImagePosInfo posInfo = new ImagePosInfo()
            {
                X = X,
                Y = Y,
                Z = Z,
                Layer = Layer
            };
            ImageDataInfo dataInfo = new ImageDataInfo() { Length = length, Offset = CurrOffset };
            //ImageInfos.TryUpdate(posInfo, dataInfo, new ImageDataInfo());
            ImageInfos.TryAdd(posInfo,dataInfo);
            CurrOffset += length;
        }

        public ConcurrentDictionary<int, byte[]> JpegCompression(Mat image)
        {
            ConcurrentDictionary<int, byte[]> jpegDataDict = new ConcurrentDictionary<int, byte[]>();
            Parallel.For(0, 3, layer =>
            {
                Mat LayerMat;
                if (layer == 0)
                {
                    LayerMat = image.Clone();
                }
                else
                {
                    LayerMat = image.Resize(new Size(0, 0), fx: 1 / Math.Pow(4, layer), fy: 1 / Math.Pow(4, layer));
                }
                byte[] layerData = LayerMat.ImEncode(".jpg");
                jpegDataDict[layer] = layerData;
            });
            return jpegDataDict;
        }

        public void SaveWSIFile(string path)
        {

            // jump to the image info offset
            Writer.Seek(DPTWSIFileConsts.ImageInfoStartOffset, SeekOrigin.Begin);
            foreach (var info in ImageInfos)
            {
                Writer.Write(info.Key.Layer);
                Writer.Write(info.Key.X);
                Writer.Write(info.Key.Y);
                Writer.Write(info.Key.Z);
                Writer.Write(info.Value.Length);
                Writer.Write(info.Value.Offset);
            }

            Writer.Close();
            DptFile.Close();
            DptFile.ChangeName(path);
        }
    }
}
