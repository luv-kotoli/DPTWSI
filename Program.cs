using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace DPTWSITest
{
    public class Program
    {
        public static void Write()
        {
            int singleImgWidth = 4992, singleImgHeight = 4992;
            int width = 50000, height = 50000;
            int overlap = 96;
            DPTWSIFileEncoder dptFile = new DPTWSIFileEncoder(121, 1, (short)singleImgWidth, (short)singleImgHeight, 
                                                              (uint)width, (uint)height, 0.173f, (uint)overlap);
            Mat Image = Cv2.ImRead("D:/yuxx/dpt_write_test/patch_test.jpg");
            Image = Image.Resize(new Size(4992,4992));
            //byte[] imageData = Image.ImEncode(".jpg");
            for (int x = 0; x< 11; x ++)
            {
               for (int y = 0; y< 11; y ++)
               {
                   int posX = x * (singleImgWidth - overlap);
                   int posY = y * (singleImgHeight - overlap);
                    Console.WriteLine($"Writing pos:{posX}-{posY}");
                   dptFile.WriteImage((uint)posX,(uint)posY,0,Image);
                   //dptFile.WriteImage(0, x, y, 0, imageData);

               }
            }
            // uint x = 0, y = 0;
            // var jpegDatas = dptFile.JpegCompression(Image);
            
            // dptFile.WriteImage(x, y, 0, Image);
            

            dptFile.SaveWSIFile("D:/yuxx/dpt_write_test/test.dpt");

        }

        public static void Read()
        {
            string filePath = "D:/yuxx/dpt_read_test/test4.dpt";
            DPTWSIFileDecoder dptFileDecoder = new DPTWSIFileDecoder(filePath);
            int readSizeX = 20000;
            int readSizeY = 20000;
            byte[] result = dptFileDecoder.ReadRegion(20000/4, 20000/4, 2000,2000, 1);
            //byte[] result = dptFileDecoder.ReadRegion(0, 0, readSizeX / 16, readSizeY / 16, 2);
            //byte[] result = dptFileDecoder.GetTile(0, 0, 4096, 0);
            Mat colorImage = new Mat(new Size(readSizeX/16, readSizeY/16), MatType.CV_8UC3, new Scalar(251, 251, 251));
            //Marshal.Copy(result, 0, colorImage.Data, result.Length);
            //colorImage.SaveImage("D:/yuxx/dpt_read_test/test_read.jpg");
        }

        public static void Main(string[] args)
        {
            //Write();
            Read();
        }
    }
}