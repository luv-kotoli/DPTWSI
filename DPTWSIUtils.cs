using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DPTWSITest
{
    public enum FusionDirection
    {
        Top,
        Bottom,
        Left,
        Right
    }
    public class DPTWSIUtils
    {
        
        public static void WeightedFusionSingle(Mat mat, int overlap, List<FusionDirection> fusionDirections)
        {
            //foreach (FusionDirection direction in fusionDirections)
            Parallel.ForEach(fusionDirections, direction =>
            {
                Point startPoint = new Point();
                Size fusionSize = new Size();
                switch (direction) // 单张图像融合区域确定
                {
                    case FusionDirection.Top:
                        startPoint = new Point(0, 0);
                        fusionSize = new Size(mat.Width, overlap);
                        break;
                    case FusionDirection.Bottom:
                        startPoint = new Point(0, mat.Height - overlap);
                        fusionSize = new Size(mat.Width, overlap);
                        break;
                    case FusionDirection.Left:
                        startPoint = new Point(0, 0);
                        fusionSize = new Size(overlap, mat.Height);
                        break;
                    case FusionDirection.Right:
                        startPoint = new Point(mat.Width - overlap, 0);
                        fusionSize = new Size(overlap, mat.Height);
                        break;
                }

                if (direction == FusionDirection.Top) // 纵向融合
                {
                    using Mat subMat = mat[new Rect(startPoint, fusionSize)];
                    Parallel.For(0, subMat.Height, y =>
                    {
                        for (int x = 0; x < subMat.Width; x++)
                        {
                            var pixsubMat = subMat.At<Vec3b>(y, x);

                            float weightsubMat = Sigmoid(y - subMat.Height / 2);

                            Vec3b fusedPixel = new Vec3b();
                            fusedPixel.Item0 = (byte)(pixsubMat.Item0 * weightsubMat);
                            fusedPixel.Item1 = (byte)(pixsubMat.Item1 * weightsubMat);
                            fusedPixel.Item2 = (byte)(pixsubMat.Item2 * weightsubMat);

                            subMat.Set(y, x, fusedPixel);

                        }
                    });
                }
                else if (direction == FusionDirection.Bottom)
                {
                    using Mat subMat = mat[new Rect(startPoint, fusionSize)];
                    Parallel.For(0, subMat.Height, y =>
                    {
                        for (int x = 0; x < subMat.Width; x++)
                        {
                            var pixsubMat = subMat.At<Vec3b>(y, x);

                            float weightsubMat = 1 - Sigmoid(y - subMat.Height / 2);

                            Vec3b fusedPixel = new Vec3b();
                            fusedPixel.Item0 = (byte)(pixsubMat.Item0 * weightsubMat);
                            fusedPixel.Item1 = (byte)(pixsubMat.Item1 * weightsubMat);
                            fusedPixel.Item2 = (byte)(pixsubMat.Item2 * weightsubMat);

                            subMat.Set(y, x, fusedPixel);

                        }
                    });
                }
                else if (direction == FusionDirection.Left)
                {
                    using Mat subMat = mat[new Rect(startPoint, fusionSize)];
                    Parallel.For(0, subMat.Width, x =>
                    {
                        for (int y = 0; y < subMat.Height; y++)
                        {
                            var pixsubMat = subMat.At<Vec3b>(y, x);

                            float weightsubMat = Sigmoid(x - subMat.Width / 2);

                            Vec3b fusedPixel = new Vec3b();
                            fusedPixel.Item0 = (byte)(pixsubMat.Item0 * weightsubMat);
                            fusedPixel.Item1 = (byte)(pixsubMat.Item1 * weightsubMat);
                            fusedPixel.Item2 = (byte)(pixsubMat.Item2 * weightsubMat);

                            subMat.Set(y, x, fusedPixel);

                        }
                    });
                }
                else // 横向融合
                {
                    using Mat subMat = mat[new Rect(startPoint, fusionSize)];
                    Parallel.For(0, subMat.Width, x =>
                    {
                        for (int y = 0; y < subMat.Height; y++)
                        {
                            var pixsubMat = subMat.At<Vec3b>(y, x);

                            float weightsubMat = 1 - Sigmoid(x - subMat.Width / 2);

                            Vec3b fusedPixel = new Vec3b();
                            fusedPixel.Item0 = (byte)(pixsubMat.Item0 * weightsubMat);
                            fusedPixel.Item1 = (byte)(pixsubMat.Item1 * weightsubMat);
                            fusedPixel.Item2 = (byte)(pixsubMat.Item2 * weightsubMat);

                            subMat.Set(y, x, fusedPixel);
                        }
                    });
                }
            //}
            });
            //Cv2.Rectangle(result, new Rect(0, 0, result.Width, result.Height), new Scalar(0, 255, 0), thickness: 2);
        }
        public static float Sigmoid(float value, double kValue = 0.3)
        {
            return 1.0f / (1.0f + (float)Math.Exp(-kValue * value));
        }

    }
}
