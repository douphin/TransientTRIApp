using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;

namespace TransientTRIApp.Common.DataProcessing
{
    public static class MatPool
    {
        private static readonly Dictionary<string, Mat> _pool = new Dictionary<string, Mat>();

        public static Mat Get(string name)
        {
            if (!_pool.TryGetValue(name, out var mat))
            {
                mat = new Mat();
                _pool[name] = mat;
            }
            return mat;
        }

        public static void Set(string name, Mat mat)
        {
            _pool[name] = mat;
        }

        public static void DisposeAll()
        {
            foreach (var mat in _pool.Values) mat.Dispose();
            _pool.Clear();
        }
    }
}
