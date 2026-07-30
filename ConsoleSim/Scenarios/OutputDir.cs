using System;
using System.IO;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// 실험 CSV 출력 디렉터리를 CWD가 아니라 저장소 루트 기준으로 고정한다.
    /// dotnet run을 어느 디렉터리에서 실행해도 output/이 한 곳에 모이도록
    /// 빌드 산출물 위치에서 ConsoleSim 디렉터리를 포함한 조상을 찾는다.
    /// </summary>
    public static class OutputDir
    {
        public static string Resolve(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConsoleSim")))
                dir = dir.Parent;
            string root = dir != null ? dir.FullName : Directory.GetCurrentDirectory();
            string output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            return Path.Combine(output, fileName);
        }
    }
}
