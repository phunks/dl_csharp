using IniParser;
using Microsoft.VisualBasic;

namespace ConsoleApp1
{
    public static class MainClass
    {
        private static void Main()
        {
            const string path = Constants.Param.IniFile;
            var parser = new FileIniDataParser();
            var iniData = parser.ReadFile(path);
            var target = iniData["Param"]["Target"];
            var result = Interaction.InputBox("demo","input box",target,100,100);
            var req = HttpRequestUtil.GetInstance();
            
            var id = req.GetDirList(Constants.Param.IniID).GetIdByNameFromDirList(result);
            if (id.Length == 0) Environment.Exit(127);
            
            req.dirPath = new List<string> { System.Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), result };

            id = req.GetDirList(id).GetIdByNameFromDirList(Constants.Param.SubDir);
            req.GetDirList(id).ToJson().RecursiveSearchFromJson();
            
            iniData["Param"]["Target"] = result;
            parser.WriteFile(path, iniData);
        }
    }
}


