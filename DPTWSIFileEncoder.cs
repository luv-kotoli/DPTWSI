using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;

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

        public void WriteImageMultiLayer(ConcurrentDictionary<int, byte[]> jpegDataDict, uint X, uint Y, byte Z)
        {
            // 遍历 jpegDataDict 中的每个元素
            foreach (var kvp in jpegDataDict)
            {
                int layer = kvp.Key;
                byte[] imageData = kvp.Value;
                int length = imageData.Length;
                Stream.Seek(CurrOffset, SeekOrigin.Begin);
                Writer.Write(imageData);

                // 创建ImageInfo对象
                ImagePosInfo posInfo = new ImagePosInfo()
                {
                    X = X,
                    Y = Y,
                    Z = Z,
                    Layer = (sbyte)layer
                };
                ImageDataInfo dataInfo = new ImageDataInfo() { Length = length, Offset = CurrOffset };
                //ImageInfos.TryUpdate(posInfo, dataInfo, new ImageDataInfo());
                ImageInfos.TryAdd(posInfo,dataInfo);
                CurrOffset += length;
            }
        }

        public void WriteImage(uint X, uint Y, byte Z, Mat imageMat)
        {
            ConcurrentDictionary<int, byte[]> jpegDataDict = JpegCompression(imageMat);
            // 调用WriteImageMultiLayer方法,将jpeg多层数据写入文件
            WriteImageMultiLayer(jpegDataDict, X, Y, Z);
        }

        public ConcurrentDictionary<int, byte[]> JpegCompression(Mat image)
        {
            ConcurrentDictionary<int, byte[]> jpegDataDict = new ConcurrentDictionary<int, byte[]>();
            List<Mat> resizedMats = new List<Mat>();
            // 使用多线程进行 Resize 操作
            var resizeTasks = new List<Task<Mat>>();

            Stopwatch sw = Stopwatch.StartNew();
            for (int layer = 0; layer < 3; layer++)
            {
                int layerCopy = layer;
                resizeTasks.Add(Task.Run(() =>
                {
                    Mat LayerMat;
                    if (layerCopy == 0)
                    {
                        LayerMat = image.Clone();
                    }
                    else
                    {
                        LayerMat = image.Resize(new Size(0, 0), fx: 1 / Math.Pow(4, layerCopy), fy: 1 / Math.Pow(4, layerCopy));
                    }
                    return LayerMat;
                }));
            }
            Task.WhenAll(resizeTasks).ContinueWith(resizeTasks => {
                foreach (var taskResult in resizeTasks.Result){
                    resizedMats.Add(taskResult);    
                }
            }).Wait();
            sw.Stop();
            Console.WriteLine($"缩放耗时：{sw.ElapsedMilliseconds}");

            sw.Restart();
            // 在线程内进行单线程JPEG压缩
            Task.Run( () => {
                for (int layer = 0; layer < resizedMats.Count; layer++){
                    Mat LayerMat = resizedMats[layer];
                    using (LayerMat){
                        byte[] layerData = LayerMat.ImEncode(".jpg");
                        jpegDataDict[layer] = layerData;
                    }
                }
            }).Wait();
            sw.Stop();
            Console.WriteLine($"JPEG压缩耗时：{sw.ElapsedMilliseconds}");
            
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
