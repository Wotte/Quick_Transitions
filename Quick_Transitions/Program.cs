// See https://aka.ms/new-console-template for more information
using Octokit;

using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static async Task Main(string[] args)
    {
        await ExportTask();
    }

    static async Task ExportTask()
    {
        bool retry = true;
        while (retry)
        {
            string selectedFilePath = "";
            Console.WriteLine("Enter .oxz file path or drag and drop the file, then press enter :");
            selectedFilePath = Console.ReadLine();
            selectedFilePath = TrimQuotes(selectedFilePath); // to ensure the quote are remove (from drag and drop)

            Console.WriteLine("Path : " + selectedFilePath);

            #region Check folders and file acess
            if (string.IsNullOrWhiteSpace(selectedFilePath))
            {
                Console.WriteLine("No file path provided.");
                retry = await AskRetry();
                continue;
            }

            if (!IsOxzFile(selectedFilePath))
            {
                Console.WriteLine("The file does not have the .oxz extension.");
                retry = await AskRetry();
                continue;
            }

            string? fileDirectory = Path.GetDirectoryName(selectedFilePath);
            if (string.IsNullOrWhiteSpace(fileDirectory))
            {
                Console.WriteLine("Couldn't find directory name at : ");
                Console.WriteLine(selectedFilePath);
                retry = await AskRetry();
                continue;
            }
            string? parentDirectory = Directory.GetParent(fileDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                Console.WriteLine("Couldn't find parent at : ");
                Console.WriteLine(fileDirectory);
                retry = await AskRetry();
                continue;
            }
            string objectsDirectory = Path.Combine(parentDirectory, "objects");
            string categoriesDirectory = Path.Combine(parentDirectory, "categories");
            string transitionsDirectory = Path.Combine(parentDirectory, "transitions");
            string dataVersionNumberFile = Path.Combine(parentDirectory, "dataVersionNumber.txt");

            if (!Directory.Exists(objectsDirectory))
            {
                Console.WriteLine("objects folder not find at : ");
                Console.WriteLine(objectsDirectory);
                retry = await AskRetry();
                continue;
            }
            if (!Directory.Exists(categoriesDirectory))
            {
                Console.WriteLine("categories folder not find at : ");
                Console.WriteLine(categoriesDirectory);
                retry = await AskRetry();
                continue;
            }
            if (!Directory.Exists(transitionsDirectory))
            {
                Console.WriteLine("transitions folder not find at : ");
                Console.WriteLine(transitionsDirectory);
                retry = await AskRetry();
                continue;
            }
            if (!File.Exists(dataVersionNumberFile))
            {
                Console.WriteLine("'dataVersionNumber.txt' not find at : ");
                Console.WriteLine(dataVersionNumberFile);
                retry = await AskRetry();
                continue;
            }
            #endregion

            string dataVersionNumberContent = File.ReadAllText(dataVersionNumberFile);

            #region Get old NextObjectNumber from Github
            string tag = "AnotherPlanet_v" + dataVersionNumberContent;

            var client = new GitHubClient(new ProductHeaderValue("Quick_Transitions"));

            var owner = "jasonrohrer";
            var repo = "AnotherPlanetData";
            var filePath = "objects/nextObjectNumber.txt";
            string oldNextObjectNumber = "";
            try
            {
                // Récupérer le contenu du fichier à partir du dépôt GitHub
                var fileContent = await client.Repository.Content.GetRawContentByRef(owner, repo, filePath, tag);

                oldNextObjectNumber = Encoding.Default.GetString(fileContent);

                Console.WriteLine("Contenu du fichier oldNextObjectNumber.txt :");
                Console.WriteLine(oldNextObjectNumber);
                Console.WriteLine($"Git request remaining : " + client.GetLastApiInfo().RateLimit.Remaining);
            }
            catch (RateLimitExceededException eLimit)
            {
                Console.WriteLine($"Git request limit");
                Console.WriteLine($"Git Limit acess (" + eLimit.Limit + " request per hour)");
                Console.WriteLine($"Next request available at " + eLimit.Reset.LocalDateTime);
                retry = await AskRetry();
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Problem during Git request : {ex.Message}");
                Console.WriteLine($"Git request remaining : "+ client.GetLastApiInfo().RateLimit.Remaining);
                retry = await AskRetry();
                continue;
            }
            #endregion

            int.TryParse(oldNextObjectNumber, out int oldNON);

            List<string> IDs = new List<string>();

            #region Get all IDs >= oldNextObjectNumber
            
            string[] Objfiles = Directory.GetFiles(objectsDirectory);
            if(Objfiles.Length == 0)
            {
                Console.WriteLine("No files at :");
                Console.WriteLine(objectsDirectory);
                retry = await AskRetry();
                continue;
            }
            foreach (string file in Objfiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                Match match = Regex.Match(fileName, @"\d+");

                if (match.Success)
                {
                    if (int.TryParse(match.Value, out int id))
                    {
                        if (id >= oldNON)
                        {
                            IDs.Add(match.Value);
                        }
                    }
                }
            }
            if(IDs.Count == 0)
            {
                Console.WriteLine("Did not find any file with number name at :");
                Console.WriteLine(objectsDirectory);
                retry = await AskRetry();
                continue;
            }
            #endregion

            IDs.Reverse();

            List<string> matchingTransitionFiles = new List<string>();

            #region Select all transitions that containt any of IDs

            string[] trafiles = Directory.GetFiles(transitionsDirectory);
            if (trafiles.Length == 0)
            {
                Console.WriteLine("No files at :");
                Console.WriteLine(transitionsDirectory);
                retry = await AskRetry();
                continue;
            }

            Regex regexTraName = new Regex(@"^(-?\d+)_(-?\d+)");
            foreach (string file in trafiles)
            {
                
                string fileName = Path.GetFileNameWithoutExtension(file);
                Match match = regexTraName.Match(fileName);
                if (match.Success)
                {
                    //first we check file Name
                    string id1 = match.Groups[1].Value;
                    string id2 = match.Groups[2].Value;

                    if (IDs.Contains(id1) || IDs.Contains(id2))
                    {
                        matchingTransitionFiles.Add(file);
                        continue; // Next file
                    }
                    // Then we check file content (first line)
                    string firstLine = File.ReadLines(file).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        string[] splitLine = firstLine.Split(' ');
                        if (splitLine.Length >= 2)
                        {
                            string id3 = splitLine[0].Trim();
                            string id4 = splitLine[1].Trim();

                            if (IDs.Contains(id3) || IDs.Contains(id4))
                            {
                                matchingTransitionFiles.Add(file);
                                //continue; // Dont need continue cause we already are at the end of the foreach
                            }
                        }
                    }
                }
                else
                {
                    //Console.WriteLine("PASS : "+ fileName);
                    continue; // We pass the file if no matching for name (for exemple cache.fcz)
                }

            }
            if (matchingTransitionFiles.Count == 0)
            {
                Console.WriteLine("Did not find any matching transitions");
                retry = await AskRetry();
                continue;
            }

            #endregion

            List<string> IDsWithCat = new List<string>(IDs);

            #region Add categories to IDs

            string[] catfiles = Directory.GetFiles(categoriesDirectory);
            if (catfiles.Length == 0)
            {
                Console.WriteLine("No file in categories folder (we pass categorie check)");
            }

            Regex regexCatName = new Regex(@"^(\d+)");
            foreach (string file in catfiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                Match match = regexCatName.Match(fileName);

                if (!match.Success) continue;
                string[] lines = File.ReadAllLines(file);
                if (lines.Length < 3)
                {
                    // Insuffisamment de lignes dans le fichier, passe au suivant
                    continue;
                }

                bool isPattern = lines[1].Contains("pattern");

                foreach (string line in lines.Skip(2)) // Start at line 3
                {
                    for (int i=0;i< IDs.Count;i++)
                    {
                        if (line == IDs[i])
                        {
                            IDsWithCat[i] += $":{fileName}=";
                            IDsWithCat[i] += isPattern ? "0" : "1";
                            IDsWithCat[i] += "/";
                            break; // cause one line can only have one ID ( witch IDs list suppose to have only different IDs)
                        }
                    }
                }

            }
            if (matchingTransitionFiles.Count == 0)
            {
                Console.WriteLine("Did not find any matching transitions");
                retry = await AskRetry();
                continue;
            }
            #endregion

            /*Console.WriteLine("ID list with categories :");
            foreach (string id in IDsWithCat)
            {
                Console.WriteLine(id);
            }*/

            #region Create .trt file

            string trtFilePath = selectedFilePath.Substring(0, selectedFilePath.Length - 4) + ".trt";
            if (File.Exists(trtFilePath))
            {
                Console.WriteLine("File at : ");
                Console.WriteLine(trtFilePath);
                Console.WriteLine("Already exist, do you want replace it ? (Y/N)");
                string response = Console.ReadLine();
                if(response.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    CreateTrtFile(matchingTransitionFiles, trtFilePath,IDsWithCat);
                }
                else
                {
                    Console.WriteLine("Exportation aborted");
                    retry = await AskRetry();
                    continue;
                }
            }
            else
            {
                CreateTrtFile(matchingTransitionFiles, trtFilePath, IDsWithCat);
            }
            #endregion
            retry = false;
        }

    }

    static void CreateTrtFile(List<string> fileList, string newFilePath, List<string> IDList)
    {
        List<string> dataList = new List<string>
            {
                GetStringFromList(IDList, ',')
            };
        foreach (var file in fileList)
        {
            dataList.Add(Path.GetFileName(file));
            foreach (var line in File.ReadLines(file))
            {
                dataList.Add(line);
            }
            dataList.Add(";;"); // To separate every files (needed when reading)
        }
        File.WriteAllLines(newFilePath, dataList);

        Console.WriteLine("Sucess");
        Console.WriteLine(fileList.Count+" transitions copy");
        Console.WriteLine("File location : ");
        Console.WriteLine(newFilePath);
    }
    static string GetStringFromList(List<string> list, char separator)
    {
        string result = "";
        for (int i = 0; i < list.Count; i++)
        {
            result += list[i];
            if (i != list.Count - 1) result += separator;
        }
        return result;
    }
    static async Task<bool> AskRetry()
    {
        Console.WriteLine("");
        Console.WriteLine("Retry ? (Y/N)");
        string response = Console.ReadLine();
        return response.Equals("Y", StringComparison.OrdinalIgnoreCase); ;
    }
    static bool IsOxzFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".oxz", StringComparison.OrdinalIgnoreCase);
    }
    static string TrimQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
        {
            return value.Substring(1, value.Length - 2);
        }
        return value;
    }
}