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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using liblistfile;
using liblistfile.Score;
using Warcraft.MPQ;
using Warcraft.TRS;

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
					Log(options, $"Generating new listfile from directory {options.InputPath}");

					using (FileStream fs = File.Create($"{options.OutputPath}{Path.DirectorySeparatorChar}(listfile)"))
					{
						using (StreamWriter sw = new StreamWriter(fs))
						{
							foreach (string path in Directory.EnumerateFiles(options.InputPath, "*", SearchOption.AllDirectories))
							{
								string childPath = path
								.Replace(options.InputPath, "")
								.TrimStart(Path.DirectorySeparatorChar)
								.Replace('/', '\\');

								Log(options, $"Found path {path}, writing path {childPath}.");

								sw.WriteLine(childPath);

								sw.Flush();
							}
						}
					}
				}
				else
				{
					Log(options, $"Optimizing archive listfiles in directory {options.InputPath}...");

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
							Log(options, $"Hashing filetable and extracting listfile of {Path.GetFileName(PackagePath)}...");
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
							// For every path in the listfile, add the terms from each folder or filename
							string[] foldersAndFile = path.Split('\\');
							for (int i = 0; i < foldersAndFile.Length; ++i)
							{
								string term = foldersAndFile[i];

								// Add the term to the dictionary
								if (ListDictionary.UpdateTermEntry(term))
								{
									++wordsUpdated;
								}

								// Some special cases with MD5 hashes - the terms for the filenames here are set to full score right away
								if (i == foldersAndFile.Length - 1)
								{
									if (path.ToLowerInvariant().StartsWith("textures\\bakednpctextures\\"))
									{
										if (ListDictionary.SetTermScore(Path.GetFileNameWithoutExtension(term), Single.MaxValue))
										{
											++wordsUpdated;
										}
									}

									if (path.ToLowerInvariant().StartsWith("textures\\minimap\\") && !path.ToLowerInvariant().EndsWith("md5translate.trs"))
									{
										if (ListDictionary.SetTermScore(Path.GetFileNameWithoutExtension(term), Single.MaxValue))
										{
											++wordsUpdated;
										}
									}
								}
							}
						}
						Log(options, $"Successfully loaded package listfile into dictionary. {wordsUpdated} words updated.");
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


						if (String.IsNullOrEmpty(userResponse))
						{
							continue;
						}
						else if (userResponse.ToUpperInvariant() == "YES" || userResponse.ToUpperInvariant() == "Y")
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
						bool quitFixing = false;

						#if true
							long totalCount = ListDictionary.LowScoreEntries.Count();
							foreach (KeyValuePair<string, ListfileDictionaryEntry> DictionaryEntry in ListDictionary.LowScoreEntries)
						#else
							string fixTerm = "Transportship";
							long totalCount = ListDictionary.HighScoreEntries.Count(pair => pair.Value.Term.Contains(fixTerm));
							foreach (KeyValuePair<string, ListfileDictionaryEntry> DictionaryEntry in ListDictionary.HighScoreEntries.Where(pair => pair.Value.Term.Contains(fixTerm)))
						#endif
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
										newTerm = TermScore.Guess(DictionaryEntry.Value.Term);
										// The user entered a valid term
										string confirmResponse = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, true, newTerm);

										if (String.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" || confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.SetTerm(newTerm);
											DictionaryEntry.Value.SetScore(Single.MaxValue);

											ListDictionary.AddNewTermWords(newTerm);

											// Go to the next term
											break;
										}
										else
										{
											// The user didn't confirm the word. Try again.
											continue;
										}
									}
									else if (newTerm == "2")
									{
										newTerm = ListDictionary.Guess(DictionaryEntry.Value.Term);
										// The user entered a valid term
										string confirmResponse = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, true, newTerm);

										if (String.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" || confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.SetTerm(newTerm);
											DictionaryEntry.Value.SetScore(Single.MaxValue);

											ListDictionary.AddNewTermWords(newTerm);

											// Go to the next term
											break;
										}
										else
										{
											// The user didn't confirm the word. Try again.
											continue;
										}
									}
									else if (newTerm == "3")
									{
										// The user requested to keep the word
										newTerm = DictionaryEntry.Value.Term;
										string confirmResponse = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, true, newTerm);

										if (String.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" ||
										    confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.SetTerm(newTerm);
											DictionaryEntry.Value.SetScore(Single.MaxValue);

											// Go to the next term
											break;
										}
										else
										{
											// The user didn't confirm the word. Try again.
											continue;
										}
									}
									else if (newTerm.ToUpperInvariant() == DictionaryEntry.Key)
									{
										// The user entered a valid term
										string confirmResponse = ShowTerm(totalCount, progressCount, DictionaryEntry, ListDictionary, true, newTerm);

										if (String.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" || confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											DictionaryEntry.Value.SetTerm(newTerm);
											DictionaryEntry.Value.SetScore(Single.MaxValue);

											ListDictionary.AddNewTermWords(newTerm);

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
						Log(options, $"Optimizing lists for {PackageName}...");

						List<string> unoptimizedList = PackageList.Value;
						List<string> optimizedList = ListDictionary.OptimizeList(unoptimizedList);

						string listContainerPath =
							$"{options.OutputPath}{Path.DirectorySeparatorChar}{PackageName}.{OptimizedListContainer.Extension}";

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
						if (listContainer.ContainsPackageListfile(PackageList.Key))
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

					if (options.Format == OutputFormat.Flatfile)
					{
						foreach (OptimizedListContainer listContainer in OptimizedListContainers)
						{
							Log(options, $"Saving lists for {listContainer.PackageName}...");

							foreach (KeyValuePair<byte[], OptimizedList> optimizedListPair in listContainer.OptimizedLists)
							{
								string packageHash = BitConverter.ToString(optimizedListPair.Value.PackageHash).Replace("-", "");

								File.WriteAllLines(
									$"{options.OutputPath}{Path.DirectorySeparatorChar}(listfile)-{listContainer.PackageName}-{packageHash}.txt",
									optimizedListPair.Value.OptimizedPaths);
							}
						}
					}
					else
					{
						foreach (OptimizedListContainer listContainer in OptimizedListContainers)
						{
							Log(options, $"Saving lists for {listContainer.PackageName}...");
							File.WriteAllBytes(
								$"{options.OutputPath}{Path.DirectorySeparatorChar}{listContainer.PackageName}.{OptimizedListContainer.Extension}",
								listContainer.GetBytes());
						}
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
			Console.WriteLine($"Progress: {termProgress} of {totalTerms}");
			Console.WriteLine("========");
			Console.WriteLine($"| Current word key: {termEntryPair.Key}");
			Console.WriteLine($"| Current word value: {termEntryPair.Value.Term}");
			Console.WriteLine($"| Current word score: {termEntryPair.Value.Score}");
			Console.WriteLine("|");
			if (confirmDialog)
			{
				Console.WriteLine($"| New word value: {newWord}");
				Console.WriteLine("| New word score (manually set): Maximum");
			}
			else
			{
				Console.WriteLine($"| [1] Guessed correct word (TermScore)\t: {TermScore.Guess(termEntryPair.Value.Term)}");
				Console.WriteLine($"| [2] Guessed correct word (Dictionary)\t: {dictionary.Guess(termEntryPair.Value.Term)}");
				Console.WriteLine($"| [3] Keep value");
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
				Console.Write("> [1/2/3/input/q]: ");
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

		private static void PrintArchiveNames(IEnumerable<string> ArchivePaths)
		{
			foreach (string ArchivePath in ArchivePaths)
			{
				Console.WriteLine($"\t* {Path.GetFileName(ArchivePath)}");
			}
		}

		private static void Log(Options options, string logString, LogLevel logLevel = LogLevel.Info, bool important = false)
		{
			if (options.Verbose || important || logLevel == LogLevel.Error)
			{
				Console.ForegroundColor = (ConsoleColor)logLevel;
				Console.WriteLine($"[{logLevel}]: {logString}");
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
