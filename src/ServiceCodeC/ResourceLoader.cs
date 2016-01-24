using System.IO;
using System.Reflection;

namespace ServiceCodeC
{
    static class ResourceLoader
    {
        public static string Load(string path)
        {
            var thisExe = Assembly.GetExecutingAssembly();
            var stream = thisExe.GetManifestResourceStream("ServiceCodeC." + path);
            var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}
