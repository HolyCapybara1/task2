using System;
using System.IO;
using System.Linq;

namespace PhotoAnnotation
{
    public static class AppPaths
    {
        public const string DbFileName = "photo.db";
        public const string ProjectDataFolder = "Data"; // 

        /// <summary>
        /// Пытаемся найти КОРЕНЬ проекта, поднимаясь вверх от папки с .exe,
        /// пока не встретим *.csproj (макс. 6 уровней). Если не нашли — вернем папку с .exe.
        /// </summary>
        public static string GetProjectRootOrExeDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                // признак корня проекта — наличие .csproj
                var hasCsproj = dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).Any();
                if (hasCsproj) return dir.FullName;
                dir = dir.Parent;
            }
            // запасной вариант — рядом с исполняемым файлом
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Полный путь к БД внутри проекта: {ProjectRoot}\Data\photo.db
        /// (папку создаём при необходимости).
        /// </summary>
        public static string GetDefaultDbPath()
        {
            var root = GetProjectRootOrExeDir();

            string baseDir = string.IsNullOrWhiteSpace(ProjectDataFolder)
                ? root
                : Path.Combine(root, ProjectDataFolder);

            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);

            var full = Path.Combine(baseDir, DbFileName);
            return Path.GetFullPath(full);
        }
    }
}
