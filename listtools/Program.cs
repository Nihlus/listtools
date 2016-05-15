//
//  Program.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2016 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using Warcraft.MPQ;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using liblistfile;
using liblistfile.Score;

namespace listtools
{
	class MainClass
	{
		public const int EXIT_SUCCESS = 0;
		public const int EXIT_FAILURE_NO_INPUT = 1;
		public const int EXIT_FAILURE_TASK_ERROR = 2;
		public const int EXIT_FAILURE_NO_DICTIONARY = 3;
		public const int EXIT_FAILURE_NO_OUTPUT = 4;

		public static void Main(string[] args)
		{
			Options options = new Options();
			if (CommandLine.Parser.Default.ParseArguments(args, options))
			{
				// Fix dictionary path if needed
				if (String.IsNullOrEmpty(options.DictionaryPath))
				{	
					options.DictionaryPath = Options.GetDefaultDictionaryPath();
				}

				// Sanity checks
				if (!Directory.Exists(options.InputPath))
				{						
					Log(options, "The input directory did not exist.", LogLevel.Error);
					Environment.Exit(EXIT_FAILURE_NO_INPUT);
				}

				if (Directory.GetFiles(options.InputPath).Length <= 0)
				{
					Log(options, "The input directory did not contain any files.", LogLevel.Error);
					Environment.Exit(EXIT_FAILURE_NO_INPUT);
				}

				if (String.IsNullOrEmpty(options.OutputPath))
				{
					Log(options, "The output directory must not be empty.", LogLevel.Error);
					Environment.Exit(EXIT_FAILURE_NO_OUTPUT);
				}

				if (!Directory.Exists(Directory.GetParent(options.DictionaryPath).FullName))
				{
					Directory.CreateDirectory(Directory.GetParent(options.DictionaryPath).FullName);
				}

				if (!options.IsUsingDefaultDictionary && !File.Exists(options.DictionaryPath))
				{
					Log(options, "The selected dictionary did not exist.", LogLevel.Error);
					Environment.Exit(EXIT_FAILURE_NO_DICTIONARY);
				}

				if (!options.DictionaryPath.EndsWith(ListfileDictionary.Extension))
				{
					Log(options, "The selected dictionary did not end with a valid extension.", LogLevel.Error);
					Environment.Exit(EXIT_FAILURE_NO_DICTIONARY);
				}

				if (!Directory.Exists(options.OutputPath))
				{
					Directory.CreateDirectory(options.OutputPath);
				}
				// End sanity checks

				if (options.SelectedTask == TaskType.Generate)
				{
					Log(options, String.Format("Generating new listfile from directory {0}", options.InputPath));

					using (FileStream fs = File.Create(String.Format("{0}{1}(listfile)", options.OutputPath, Path.DirectorySeparatorChar)))
					{
						using (StreamWriter sw = new StreamWriter(fs))
						{
							foreach (string path in Directory.EnumerateFiles(options.InputPath, "*", SearchOption.AllDirectories))
							{
								string childPath = path
								.Replace(options.InputPath, "")
								.TrimStart(Path.DirectorySeparatorChar)
								.Replace('/', '\\');

								Log(options, String.Format("Found path {0}, writing path {1}.", path, childPath));

								sw.WriteLine(childPath);

								sw.Flush();
							}						
						}
					}
				}
				else
				{
					Log(options, String.Format("Optimizing archive listfiles in directory {0}...", options.InputPath));

					ListfileDictionary ListDictionary;
					if (File.Exists(options.DictionaryPath))
					{
						Log(options, "Loading dictionary...");
						ListDictionary = new ListfileDictionary(File.ReadAllBytes(options.DictionaryPath));
					}
					else
					{
						Log(options, "Creating new dictionary...");
						ListDictionary = new ListfileDictionary();
					}

					Log(options, "Loading packages...");

					List<string> PackagePaths = Directory.EnumerateFiles(options.InputPath, "*.*", SearchOption.AllDirectories)
					.OrderBy(a => a)
					.Where(s => s.EndsWith(".mpq") || s.EndsWith(".MPQ"))
					.ToList();

					if (PackagePaths.Count <= 0)
					{
						Log(options, "No game packages were found in the input directory.", LogLevel.Warning);
						Environment.Exit(EXIT_FAILURE_NO_INPUT);
					}

					PackagePaths.Sort();
					Log(options, "Packages found: ");
					PrintArchiveNames(PackagePaths);

					// Load the archives with their hashes and listfiles
					Dictionary<byte[], List<string>> PackageLists = new Dictionary<byte[], List<string>>();
					Dictionary<byte[], string> PackageNames = new Dictionary<byte[], string>();
					foreach (string PackagePath in PackagePaths)
					{
						// Hash the hash table and extract the listfile
						byte[] md5Hash;
						List<string> PackageListfile; 
						using (MPQ Package = new MPQ(File.OpenRead(PackagePath)))
						{
							Log(options, String.Format("Hashing filetable and extracting listfile of {0}...", Path.GetFileName(PackagePath)));
							using (MD5 md5 = MD5.Create())
							{
								md5Hash = md5.ComputeHash(Package.ArchiveHashTable.Serialize());
							}
							PackageListfile = Package.GetFileList();
						}

						PackageLists.Add(md5Hash, PackageListfile);
						PackageNames.Add(md5Hash, Path.GetFileNameWithoutExtension(PackagePath));
					}

					// Populate dictionary
					foreach (KeyValuePair<byte[], List<string>> PackageList in PackageLists)
					{
						long wordsUpdated = 0;
						foreach (string path in PackageList.Value)
						{							
							foreach (string word in path.Split('\\'))
							{
								if (ListDictionary.UpdateWordEntry(word))
								{
									++wordsUpdated;
								}
							}
						}
						Log(options, String.Format("Successfully loaded package listfile into dictionary. {0} words updated.", wordsUpdated));
					}

					// Manual dictionary fixing
					Log(options, "The dictionary has been populated. At this point, you may optionally fix some entries with low scores.", LogLevel.Info, true);
					while (true)
					{
						Console.WriteLine("Fix low-score entries? [y/N]: ");
						string userResponse = Console.ReadLine();

						if (userResponse.ToUpperInvariant() == "YES" || userResponse.ToUpperInvariant() == "Y")
						{							
							Console.WriteLine("(Hint: A score of 0 usually means an all-caps entry. 0.5 will include any entries which are all lower-case.)");						
							Console.WriteLine("Enter a threshold floating-point score to be used: ");
							float scoreTolerance = 0;
							while (!Single.TryParse(Console.ReadLine(), out scoreTolerance))
							{
								Console.Clear();
								Console.WriteLine("Please enter a valid numeric value.");
							}


							ListDictionary.EntryScoreTolerance = scoreTolerance;

							long progressCount = 0;
							long totalCount = ListDictionary.LowScoreEntries.Count();
							foreach (KeyValuePair<string, ListfileDictionaryEntry> DictionaryEntry in ListDictionary.LowScoreEntries)
							{
								string newWord = "";
								bool restartWordError = false;
								++progressCount;
								while (true)
								{												
									Console.Clear();
									Console.WriteLine("========");
									Console.WriteLine(String.Format("Progress: {0} of {1}", progressCount, totalCount));
									Console.WriteLine("========");
									Console.WriteLine(String.Format("| Current word key: {0}", DictionaryEntry.Key));
									Console.WriteLine(String.Format("| Current word value: {0}", DictionaryEntry.Value.Word));
									Console.WriteLine(String.Format("| Current word score: {0}", DictionaryEntry.Value.Score));
									Console.WriteLine("|");
									Console.WriteLine(String.Format("| Guessed correct word: {0}", WordScore.Guess(DictionaryEntry.Value.Word)));
									if (restartWordError)
									{
										Console.WriteLine("| Error: The new word must be the same as the old word. Only casing may be altered.");
									}
									Console.WriteLine("========");

									Console.Write("> : ");
									newWord = Console.ReadLine();

									if (!String.IsNullOrEmpty(newWord) && newWord.ToUpperInvariant() == DictionaryEntry.Key)
									{
										Console.Clear();
										Console.WriteLine("========");
										Console.WriteLine(String.Format("Progress: {0} of {1}", progressCount, totalCount));
										Console.WriteLine("========");
										Console.WriteLine(String.Format("| Current word key: {0}", DictionaryEntry.Key));
										Console.WriteLine(String.Format("| Current word value: {0}", DictionaryEntry.Value.Word));
										Console.WriteLine(String.Format("| Current word score: {0}", DictionaryEntry.Value.Score));
										Console.WriteLine("|");
										Console.WriteLine(String.Format("| New word value: {0}", newWord));
										Console.WriteLine(String.Format("| New word score (manually set): Maximum"));
										Console.WriteLine("========");
										Console.Write("Confirm? [Y/n] : ");

										string confirmResponse = Console.ReadLine();

										if (confirmResponse.ToUpperInvariant() != "NO" || confirmResponse.ToUpperInvariant() != "N")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.ForceUpdateWord(newWord, Single.MaxValue);

											//Save the current dictionary
											File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());
											break;
										}
										else
										{
											// No confirmation, try again.
											continue;
										}
									}
									else if (!String.IsNullOrEmpty(newWord))
									{
										// The word the user entered was malformed or didn't match the original word.
										restartWordError = true;
									}
									else
									{
										// The guessed word was fine, so use that.					
										DictionaryEntry.Value.ForceUpdateWord(WordScore.Guess(DictionaryEntry.Value.Word), Single.MaxValue);
										break;
									}
								}
							}
							break;
						}
						else if (userResponse.ToUpperInvariant() == "NO" || userResponse.ToUpperInvariant() == "N")
						{
							break;
						}
						else
						{
							Console.Clear();
						}
					}
					//Save the current dictionary
					File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());


					// Optimize lists using dictionary
					List<OptimizedListContainer> OptimizedLists = new List<OptimizedListContainer>();
					foreach (KeyValuePair<byte[], List<string>> PackageList in PackageLists)
					{
						string PackageName = PackageNames[PackageList.Key];
						List<string> unoptimizedList = PackageList.Value;

						List<string> optimizedList = ListDictionary.OptimizeList(unoptimizedList);

						OptimizedListContainer listContainer = new OptimizedListContainer(PackageName);
						listContainer.AddOptimizedList(new OptimizedList(PackageList.Key, optimizedList));

						OptimizedLists.Add(listContainer);
					}

					// Save the new lists
					foreach (OptimizedListContainer listContainer in OptimizedLists)
					{
						File.WriteAllBytes(String.Format("{0}{1}{2}.{3}", options.OutputPath, 
								Path.DirectorySeparatorChar, 
								listContainer.ArchiveName, 
								OptimizedListContainer.Extension), listContainer.GetBytes());
					}
				}
			}
			else
			{
				Console.WriteLine(options.GetUsage());
			}

			// Read the input arguments
			// If optimizing, do
			// Load provided dictionary OR create new one
			// Get all archive paths from directory pointed to

			// foreach archive
			// Load archive
			// If optimized format, get weak signature of the archive and store with listfile
			// Get listfile of the archive
			// Load all words from the listfile into the dictionary
			// end

			// foreach listfile
			// Replace each word with a lower score with the better word
			// Store listfile as specified output format (optimized or flatfile)
			// end

					
			// If generating, do
			// Get all file paths from directory pointed to
			// Store all paths in the specified output format (optimized or flatfile)

			Environment.Exit(EXIT_SUCCESS);
		}

		private static void PrintArchiveNames(List<string> ArchivePaths)
		{
			foreach (string ArchivePath in ArchivePaths)
			{	
				Console.WriteLine(String.Format("\t* {0}", Path.GetFileName(ArchivePath)));
			}
		}

		private static void Log(Options options, string logString, LogLevel logLevel = LogLevel.Info, bool important = false)
		{
			if (options.Verbose || important || logLevel == LogLevel.Error)
			{
				Console.ForegroundColor = (ConsoleColor)logLevel;
				Console.WriteLine(String.Format("[{0}]: {1}", logLevel, logString));
			}
		}
	}

	/// <summary>
	/// The logging level.
	/// </summary>
	public enum LogLevel
	{
		/// <summary>
		/// Information. Standard output.
		/// </summary>
		Info = ConsoleColor.White,

		/// <summary>
		/// Warnings. Yellow, considered level 2.
		/// </summary>
		Warning = ConsoleColor.Yellow,

		/// <summary>
		/// Errors. Red, will exit when thrown.
		/// </summary>
		Error = ConsoleColor.Red,
	}
}
