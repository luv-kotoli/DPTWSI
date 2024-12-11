using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using OpenCvSharp;

namespace DPTWSITest
{
    public class Program
    {
        public static void Write()
        {
            DPTWSIFileEncoder dptFile = new DPTWSIFileEncoder(1, 1, 5000, 5000, 5000, 5000, 0.173f, 100);
            Mat Image = Cv2.ImRead("D:/yuxx/dpt_write_test/patch_test.jpg");
            Image = Image.Resize(new Size(5000, 5000));
            //byte[] imageData = Image.ImEncode(".jpg");
            //for (uint x = 0; x< 5000*20; x += 5000)
            //{
            //    for (uint  y = 0; y< 5000 * 20; y += 5000)
            //    {
            //        //dptFile.WriteImage(0, x, y, 0, imageData);

            //    }
            //}
            uint x = 0, y = 0;
            var jpegDatas = dptFile.JpegCompression(Image);
            for (sbyte Layer = 0; Layer < 3; Layer++)
            {
                dptFile.WriteImage(Layer, x, y, 0, jpegDatas[Layer]);
            }

            dptFile.SaveWSIFile("D:/yuxx/dpt_write_test/test.dpt");

        }

        public static void Read()
        {
            string filePath = "D:/yuxx/dpt_write_test/test.dpt";
            DPTWSIFileDecoder dptFileDecoder = new DPTWSIFileDecoder(filePath);
        }

        public static void Main(string[] args)
        {
            Read();
        }
    }
}