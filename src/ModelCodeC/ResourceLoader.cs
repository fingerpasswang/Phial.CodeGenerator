using System.IO;
using System.Reflection;

namespace ModelCodeC
{
    static class ResourceLoader
    {
        public static string Load(string path)
        {
            var thisExe = Assembly.GetExecutingAssembly();
            var stream = thisExe.GetManifestResourceStream("ModelCodeC." + path);
            var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}
