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
								if (ListDictionary.UpdateTermEntry(word))
								{
									++wordsUpdated;
								}
							}
						}
						Log(options, String.Format("Successfully loaded package listfile into dictionary. {0} words updated.", wordsUpdated));
					}

					// Manual dictionary fixing
					bool bShouldFix = false;
					float scoreTolerance = 0;
					while (true)
					{
						Console.Clear();
						Log(options, "The dictionary has been populated. At this point, you may optionally fix some entries with low scores.", LogLevel.Info, true);
						Console.WriteLine("Fix low-score entries? [y/N]: ");
						string userResponse = Console.ReadLine();

						if (userResponse.ToUpperInvariant() == "YES" || userResponse.ToUpperInvariant() == "Y")
						{						
							Console.Clear();
							Console.WriteLine("(Hint: A score of 0 usually means an all-caps entry. 0.5 will include any entries which are all lower-case.)");						
							Console.WriteLine("Enter a threshold floating-point score to be used: ");
							while (!Single.TryParse(Console.ReadLine(), out scoreTolerance))
							{
								Console.Clear();
								Console.WriteLine("Please enter a valid numeric value.");
							}

							bShouldFix = true;

							break;
						}
						else if (String.IsNullOrEmpty(userResponse))
						{
							continue;
						}
						else
						{
							Console.Clear();
							break;
						}
					}

					if (bShouldFix)
					{
						ListDictionary.EntryLowScoreTolerance = scoreTolerance;
						long progressCount = 0;
						long totalCount = ListDictionary.LowScoreEntries.Count();
						bool quitFixing = false;

						foreach (KeyValuePair<string, ListfileDictionaryEntry> DictionaryEntry in ListDictionary.LowScoreEntries)
						//foreach (KeyValuePair<string, ListfileDictionaryEntry> DictionaryEntry in ListDictionary.HighScoreEntries.Where(pair => pair.Value.Term.Contains("Smpint")))
						{
							if (quitFixing)
							{
								break;
							}

							bool restartWordError = false;
							++progressCount;
							while (true)
							{
								// Display the term to the user with options										
								string newTerm = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, false, "", restartWordError);

								if (newTerm.ToUpperInvariant() == "QUIT" || newTerm.ToUpperInvariant() == "Q")
								{
									//Save the current dictionary
									Log(options, "Saving dictionary and quitting...");
									File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());
									quitFixing = true;
									break;
								}
								else if (!String.IsNullOrEmpty(newTerm))
								{
									// The user replied with one of the two options, or a malfored word
									if (newTerm == "1")
									{
										// The guessed term (using TermScore) was fine, so use that.					
										DictionaryEntry.Value.ForceUpdateTerm(TermScore.Guess(DictionaryEntry.Value.Term), Single.MaxValue);
										ListDictionary.AddNewTermWords(TermScore.Guess(DictionaryEntry.Value.Term));

										//Save the current dictionary
										Log(options, "Saving dictionary...");
										File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());

										// Continue to the next word
										break;						
									}
									else if (newTerm == "2")
									{
										// The guessed term (using ListfileDictionary) was fine, so use that.					
										DictionaryEntry.Value.ForceUpdateTerm(ListDictionary.Guess(DictionaryEntry.Value.Term), Single.MaxValue);
										ListDictionary.AddNewTermWords(ListDictionary.Guess(DictionaryEntry.Value.Term));

										//Save the current dictionary
										Log(options, "Saving dictionary...");
										File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());

										// Continue to the next word
										break;											
									}
									else if (newTerm.ToUpperInvariant() == DictionaryEntry.Key)
									{
										// The user entered a valid term
										string confirmResponse = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, true, newTerm);

										if (String.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" || confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.ForceUpdateTerm(newTerm, Single.MaxValue);
											ListDictionary.AddNewTermWords(newTerm);

											//Save the current dictionary
											Log(options, "Saving dictionary...");
											File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());

											// Go to the next term
											break;
										}
										else
										{
											// The user didn't confirm the word. Try again.
											continue;
										}
									}
									else
									{
										// The word the user entered was malformed or didn't match the original word.
										restartWordError = true;
										continue;
									}
								}
							}
						}
					}

					// Optimize lists using dictionary
					Log(options, "Optimizing lists using dictionary...", LogLevel.Info, true);
					List<OptimizedListContainer> OptimizedListContainers = new List<OptimizedListContainer>();
					foreach (KeyValuePair<byte[], List<string>> PackageList in PackageLists)
					{						
						string PackageName = PackageNames[PackageList.Key];
						Log(options, String.Format("Optimizing lists for {0}...", PackageName));

						List<string> unoptimizedList = PackageList.Value;
						List<string> optimizedList = ListDictionary.OptimizeList(unoptimizedList);

						string listContainerPath = String.Format("{0}{1}{2}.{3}", options.OutputPath, 
							                           Path.DirectorySeparatorChar, 
							                           PackageName, 
							                           OptimizedListContainer.Extension);

						OptimizedListContainer listContainer;
						if (File.Exists(listContainerPath))
						{
							listContainer = new OptimizedListContainer(File.ReadAllBytes(listContainerPath));
						}
						else
						{
							listContainer = new OptimizedListContainer(PackageName);
						}


						OptimizedList newList = new OptimizedList(PackageList.Key, optimizedList);
						if (listContainer.ContainsPackageList(PackageList.Key))
						{
							if (!listContainer.IsListSameAsStored(newList))
							{
								listContainer.ReplaceOptimizedList(newList);
							}
						}
						else
						{
							listContainer.AddOptimizedList(newList);
						}

						OptimizedListContainers.Add(listContainer);
					}

					// Save the new lists
					Log(options, "Saving optimized lists...", LogLevel.Info, true);
					foreach (OptimizedListContainer listContainer in OptimizedListContainers)
					{
						Log(options, String.Format("Saving lists for {0}...", listContainer.PackageName));
						File.WriteAllBytes(String.Format("{0}{1}{2}.{3}", options.OutputPath, 
								Path.DirectorySeparatorChar, 
								listContainer.PackageName, 
								OptimizedListContainer.Extension), listContainer.GetBytes());
					}

					//Save the current dictionary
					Log(options, "Saving dictionary...");
					File.WriteAllBytes(options.DictionaryPath, ListDictionary.GetBytes());
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

		private static string ShowTerm(long totalTerms, long termProgress, KeyValuePair<string,
		                             ListfileDictionaryEntry> termEntryPair,
		                               ListfileDictionary dictionary, 
		                               bool confirmDialog = false,
		                               string newWord = "",
		                               bool restartError = false)
		{
			Console.Clear();
			Console.WriteLine("========");
			Console.WriteLine(String.Format("Progress: {0} of {1}", termProgress, totalTerms));
			Console.WriteLine("========");
			Console.WriteLine(String.Format("| Current word key: {0}", termEntryPair.Key));
			Console.WriteLine(String.Format("| Current word value: {0}", termEntryPair.Value.Term));
			Console.WriteLine(String.Format("| Current word score: {0}", termEntryPair.Value.Score));
			Console.WriteLine("|");
			if (confirmDialog)
			{
				Console.WriteLine(String.Format("| New word value: {0}", newWord));
				Console.WriteLine(String.Format("| New word score (manually set): Maximum"));
			}
			else
			{
				Console.WriteLine(String.Format("| [1] Guessed correct word (TermScore)\t: {0}", TermScore.Guess(termEntryPair.Value.Term)));
				Console.WriteLine(String.Format("| [2] Guessed correct word (Dictionary)\t: {0}", dictionary.Guess(termEntryPair.Value.Term)));
			}

			if (restartError)
			{
				Console.WriteLine("| Error: The new word must be the same as the old word. Only casing may be altered.");
			}

			Console.WriteLine("========");

			if (confirmDialog)
			{
				Console.Write("> [Y/n]: ");
			}
			else
			{
				Console.WriteLine("Default: Dictionary");
				Console.Write("> [1/2/input/q]: ");
			}

			string input = Console.ReadLine();
			if (String.IsNullOrEmpty(input))
			{
				if (confirmDialog)
				{
					return "Y";
				}
				else
				{
					return "2";
				}
			}
			else
			{
				return input;
			}
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
