using System;

namespace HoPmxDataValidation
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--ray-pmx"))
            {
                Console.Error.WriteLine("Usage: dotnet run --project Tools~/PmxImport/DataValidation -- [--ray-pmx <cloud-scene.pmx>]");
                return 2;
            }
            try
            {
                SourceImageValidation.Run();
                RayValidation.RunSynthetic();
                if (args.Length == 2) RayValidation.RunCloudPackage(args[1]);
                Console.WriteLine("PASS PMX source-image and Ray data validation");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception.Message);
                return 1;
            }
        }
    }
}
