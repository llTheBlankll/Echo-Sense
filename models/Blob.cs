using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echo_Sense.models
{
    // Blob class to store information about a blob
    public class Blob
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public double Area { get; set; }
        public int ID { get; set; }

        public Blob(int id, float centerX, float centerY, double area)
        {
            CenterX = centerX;
            CenterY = centerY;
            Area = area;
            ID = id;
        }

        public void Update(float centerX, float centerY, double area)
        {
            CenterX = centerX;
            CenterY = centerY;
            Area = area;

        }
    }
}
