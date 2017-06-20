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
using CommandLine;
using liblistfile;
using liblistfile.Score;
using Warcraft.MPQ;

namespace listtools
{
	internal static class Program
	{
		private const int ExitSuccess = 0;
		private const int ExitFailureNoInput = 1;
		private const int ExitFailureTaskError = 2;
		private const int ExitFailureNoDictionary = 3;
		private const int ExitFailureNoOutput = 4;

		public static void Main(string[] args)
		{
			Options options = new Options();
			if (Parser.Default.ParseArguments(args, options))
			{
				Console.Clear();

				// Fix dictionary path if needed
				if (string.IsNullOrEmpty(options.DictionaryPath))
				{
					options.DictionaryPath = Options.GetDefaultDictionaryPath();
				}

				PerformSanityChecks(options);

				switch (options.SelectedTask)
				{
					case TaskType.Generate:
					{
						GenerateListfile(options);
						break;
					}
					case TaskType.Optimize:
					{
						Log(options, $"Optimizing archive listfiles in directory {options.InputPath}...");

						ListfileDictionary listDictionary;
						if (File.Exists(options.DictionaryPath))
						{
							Log(options, "Loading dictionary...");
							listDictionary = new ListfileDictionary(File.ReadAllBytes(options.DictionaryPath));
						}
						else
						{
							Log(options, "Creating new dictionary...");
							listDictionary = new ListfileDictionary();
						}

						Log(options, "Loading packages...");

						List<string> packagePaths = Directory.EnumerateFiles(options.InputPath, "*.*", SearchOption.AllDirectories)
							.OrderBy(a => a)
							.Where(s => s.EndsWith(".mpq") || s.EndsWith(".MPQ"))
							.ToList();

						if (packagePaths.Count <= 0)
						{
							Log(options, "No game packages were found in the input directory.", LogLevel.Warning);
							Environment.Exit(ExitFailureNoInput);
						}

						packagePaths.Sort();
						Log(options, "Packages found: ");
						PrintArchiveNames(packagePaths);

						// Load the archives with their hashes and listfiles
						Dictionary<byte[], List<string>> packageLists = new Dictionary<byte[], List<string>>();
						Dictionary<byte[], string> packageNames = new Dictionary<byte[], string>();

						foreach (string packagePath in packagePaths)
						{
							// Hash the hash table and extract the listfile
							byte[] md5Hash;
							List<string> packageListfile;
							using (MPQ package = new MPQ(File.OpenRead(packagePath)))
							{
								Log(options, $"Hashing filetable and extracting listfile of {Path.GetFileName(packagePath)}...");
								using (MD5 md5 = MD5.Create())
								{
									md5Hash = md5.ComputeHash(package.ArchiveHashTable.Serialize());
								}
								packageListfile = package.GetFileList();
							}

							packageLists.Add(md5Hash, packageListfile);
							packageNames.Add(md5Hash, Path.GetFileNameWithoutExtension(packagePath));
						}

						// Populate dictionary
						foreach (KeyValuePair<byte[], List<string>> packageList in packageLists)
						{
							long wordsUpdated = 0;
							foreach (string path in packageList.Value)
							{
								// For every path in the listfile, add the terms from each folder or filename
								string[] foldersAndFile = path.Split('\\');
								for (int i = 0; i < foldersAndFile.Length; ++i)
								{
									string term = foldersAndFile[i];

									// Add the term to the dictionary
									if (listDictionary.UpdateTermEntry(term))
									{
										++wordsUpdated;
									}

									// Some special cases with MD5 hashes - the terms for the filenames here are set to full score right away
									if (i == foldersAndFile.Length - 1)
									{
										if (path.ToLowerInvariant().StartsWith("textures\\bakednpctextures\\"))
										{
											if (listDictionary.SetTermScore(Path.GetFileNameWithoutExtension(term), float.MaxValue))
											{
												++wordsUpdated;
											}
										}

										if (path.ToLowerInvariant().StartsWith("textures\\minimap\\") && !path.ToLowerInvariant().EndsWith("md5translate.trs"))
										{
											if (listDictionary.SetTermScore(Path.GetFileNameWithoutExtension(term), float.MaxValue))
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


							if (string.IsNullOrEmpty(userResponse))
							{
								continue;
							}

							if (userResponse.ToUpperInvariant() == "YES" || userResponse.ToUpperInvariant() == "Y")
							{
								Console.Clear();
								Console.WriteLine("(Hint: A score of 0 usually means an all-caps entry. 0.5 will include any entries which are all lower-case.)");
								Console.WriteLine("Enter a threshold floating-point score to be used: ");
								while (!float.TryParse(Console.ReadLine(), out scoreTolerance))
								{
									Console.Clear();
									Console.WriteLine("Please enter a valid numeric value.");
								}

								bShouldFix = true;

								break;
							}

							Console.Clear();
							break;
						}

						if (bShouldFix)
						{
							listDictionary.EntryLowScoreTolerance = scoreTolerance;
							long progressCount = 0;
							bool quitFixing = false;

							long totalCount = listDictionary.LowScoreEntries.Count();
							foreach (KeyValuePair<string, ListfileDictionaryEntry> dictionaryEntry in listDictionary.LowScoreEntries
									.OrderBy(e => e.Value.Term.Length))
									//.ThenBy(e => e.Value.Term))
							//var invalidCompounds = listDictionary.HighScoreEntries.Where(e => e.Key.Contains("MULLGORE")).ToList();
							//long totalCount = invalidCompounds.Count();
							//foreach(var dictionaryEntry in invalidCompounds)
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
									string newTerm = ShowTerm(totalCount, progressCount, dictionaryEntry, listDictionary, false, "", restartWordError);

									if (newTerm.ToUpperInvariant() == "QUIT" || newTerm.ToUpperInvariant() == "Q")
									{
										//Save the current dictionary
										Log(options, "Saving dictionary and quitting...");
										File.WriteAllBytes(options.DictionaryPath, listDictionary.Serialize());
										quitFixing = true;
										break;
									}

									if (!string.IsNullOrEmpty(newTerm))
									{
										switch (newTerm)
										{
											case "1": // TermScore
											{
												newTerm = TermScore.Guess(dictionaryEntry.Value.Term);
												break;
											}
											case "2": // Dictionary
											{
												newTerm = listDictionary.Guess(dictionaryEntry.Value.Term);
												break;
											}
											case "3": // Hybrid
											{
												newTerm = listDictionary.Guess(TermScore.Guess(dictionaryEntry.Value.Term));
												break;
											}
											case "4": // Keep
											{
												newTerm = dictionaryEntry.Value.Term;
												break;
											}
											case "5": // Correct compound word
											{
												CorrectCompoundWord(listDictionary, dictionaryEntry.Value);
												continue;
											}
											default: // Invalid
											{
												if (newTerm.ToUpperInvariant() != dictionaryEntry.Key)
												{
													restartWordError = true;
													continue;
												}

												break;
											}
										}

										// The user entered a valid term
										string confirmResponse = ShowTerm(totalCount, progressCount, dictionaryEntry, listDictionary, true, newTerm);

										if (string.IsNullOrEmpty(confirmResponse) || confirmResponse.ToUpperInvariant() == "YES" || confirmResponse.ToUpperInvariant() == "Y")
										{
											// Positive confirmation, save the new word.
											dictionaryEntry.Value.SetTerm(newTerm);
											dictionaryEntry.Value.SetScore(float.MaxValue);

											listDictionary.AddNewTermWords(newTerm);

											// Go to the next term
											break;
										}
									}
								}
							}
						}

						// Optimize lists using dictionary
						Log(options, "Optimizing lists using dictionary...", LogLevel.Info, true);
						List<OptimizedListContainer> optimizedListContainers = new List<OptimizedListContainer>();
						foreach (KeyValuePair<byte[], List<string>> packageList in packageLists)
						{
							string packageName = packageNames[packageList.Key];
							Log(options, $"Optimizing lists for {packageName}...");

							List<string> unoptimizedList = packageList.Value;
							List<string> optimizedList = listDictionary.OptimizeList(unoptimizedList).ToList();

							string listContainerPath =
								$"{options.OutputPath}{Path.DirectorySeparatorChar}{packageName}.{OptimizedListContainer.Extension}";

							OptimizedListContainer listContainer;
							if (File.Exists(listContainerPath))
							{
								listContainer = new OptimizedListContainer(File.ReadAllBytes(listContainerPath));
							}
							else
							{
								listContainer = new OptimizedListContainer(packageName);
							}


							OptimizedList newList = new OptimizedList(packageList.Key, optimizedList);
							if (listContainer.ContainsPackageListfile(packageList.Key))
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

							optimizedListContainers.Add(listContainer);
						}

						// Save the new lists
						Log(options, "Saving optimized lists...", LogLevel.Info, true);

						if (options.Format == OutputFormat.Flatfile)
						{
							foreach (OptimizedListContainer listContainer in optimizedListContainers)
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
							foreach (OptimizedListContainer listContainer in optimizedListContainers)
							{
								Log(options, $"Saving lists for {listContainer.PackageName}...");
								File.WriteAllBytes(
									$"{options.OutputPath}{Path.DirectorySeparatorChar}{listContainer.PackageName}.{OptimizedListContainer.Extension}",
									listContainer.Serialize());
							}
						}

						//Save the current dictionary
						Log(options, "Saving dictionary...");
						File.WriteAllBytes(options.DictionaryPath, listDictionary.Serialize());
						break;
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

			Environment.Exit(ExitSuccess);
		}

		private static void CorrectCompoundWord(ListfileDictionary dictionary, ListfileDictionaryEntry entry)
		{
			bool wasGivenNewTermNonexistent = false;
			while (true)
			{
				Console.Clear();
				Console.WriteLine("========");
				Console.WriteLine("Correcting compound word.");
				Console.WriteLine("========");
				Console.WriteLine($"| Hint term: {entry.Term}");
				Console.WriteLine($"|");
				Console.WriteLine($"| Please enter a corrected term or composite term.");
				Console.WriteLine("========");
				if (wasGivenNewTermNonexistent)
				{
					Console.WriteLine($"| Error: The corrected term did not match a term already in the dictionary.");
				}
				Console.Write("> [input/q]: ");

				string userInput = Console.ReadLine();
				if (string.IsNullOrEmpty(userInput))
				{
					continue;
				}

				if (userInput.ToUpperInvariant() == "QUIT" || userInput.ToUpperInvariant() == "Q")
				{
					return;
				}

				if (!dictionary.ContainsTerm(userInput))
				{
					wasGivenNewTermNonexistent = true;
					continue;
				}
				wasGivenNewTermNonexistent = false;

				List<string> newTerms = ListfileDictionary.GetWordsFromTerm(userInput).ToList();

				Console.Clear();
				Console.WriteLine("========");
				Console.WriteLine("Correcting compound word.");
				Console.WriteLine("========");
				Console.WriteLine($"| Hint term: {entry.Term}");
				Console.WriteLine($"|");
				Console.WriteLine($"| Processing the new term produced the following composite terms.");
				foreach (string term in newTerms)
				{
					Console.WriteLine($"| · {term}");
				}
				Console.WriteLine($"|");
				Console.WriteLine($"| Is this correct? If yes, the old term will be replaced by these composite terms.");
				Console.WriteLine($"| Optionally, you can delete the old term without adding new terms.");
				Console.WriteLine("========");
				Console.Write("> [y/d/N]: ");

				string confirmNew = Console.ReadLine();
				if (string.IsNullOrEmpty(confirmNew)
				    || confirmNew.ToUpperInvariant() != "YES"
				    || confirmNew.ToUpperInvariant() != "Y"
				    || confirmNew.ToUpperInvariant() != "DELETE"
				    || confirmNew.ToUpperInvariant() != "D")
				{
					continue;
				}

				// Terms are OK
				dictionary.DeleteTerm(userInput);

				if (confirmNew.ToUpperInvariant() == "YES" || confirmNew.ToUpperInvariant() == "Y")
				{
					dictionary.AddNewTermWords(userInput);
				}

				break;
			}
		}

		private static void PerformSanityChecks(Options options)
		{
			if (!Directory.Exists(options.InputPath))
			{
				Log(options, "The input directory did not exist.", LogLevel.Error);
				Environment.Exit(ExitFailureNoInput);
			}

			if (Directory.GetFiles(options.InputPath).Length <= 0)
			{
				Log(options, "The input directory did not contain any files.", LogLevel.Error);
				Environment.Exit(ExitFailureNoInput);
			}

			if (string.IsNullOrEmpty(options.OutputPath))
			{
				Log(options, "The output directory must not be empty.", LogLevel.Error);
				Environment.Exit(ExitFailureNoOutput);
			}

			if (!Directory.Exists(Directory.GetParent(options.DictionaryPath).FullName))
			{
				Directory.CreateDirectory(Directory.GetParent(options.DictionaryPath).FullName);
			}

			if (!options.IsUsingDefaultDictionary && !File.Exists(options.DictionaryPath))
			{
				Log(options, "The selected dictionary did not exist.", LogLevel.Error);
				Environment.Exit(ExitFailureNoDictionary);
			}

			if (!options.DictionaryPath.EndsWith(ListfileDictionary.Extension))
			{
				Log(options, "The selected dictionary did not end with a valid extension.", LogLevel.Error);
				Environment.Exit(ExitFailureNoDictionary);
			}

			if (!Directory.Exists(options.OutputPath))
			{
				Directory.CreateDirectory(options.OutputPath);
			}
		}

		private static void GenerateListfile(Options options)
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
			Console.WriteLine($"|");
			if (confirmDialog)
			{
				Console.WriteLine($"| New word value: {newWord}");
				Console.WriteLine($"| New word score (manually set): Maximum");
			}
			else
			{
				Console.WriteLine($"| [1] Guessed correct word (TermScore)\t: {TermScore.Guess(termEntryPair.Value.Term)}");
				Console.WriteLine($"| [2] Guessed correct word (Dictionary)\t: {dictionary.Guess(termEntryPair.Value.Term)}");
				Console.WriteLine($"| [3] Guessed correct word (Hybrid)\t: {dictionary.Guess(TermScore.Guess(termEntryPair.Value.Term))}");
				Console.WriteLine($"| [4] Keep value");
				Console.WriteLine($"| [5] Correct compound term");
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
				Console.WriteLine("Default: Hybrid");
				Console.Write("> [1/2/3/4/5/input/q]: ");
			}

			string input = Console.ReadLine();
			if (string.IsNullOrEmpty(input))
			{
				if (confirmDialog)
				{
					return "Y";
				}

				return "3";
			}

			return input;
		}

		private static void PrintArchiveNames(IEnumerable<string> archivePaths)
		{
			foreach (string archivePath in archivePaths)
			{
				Console.WriteLine($"\t* {Path.GetFileName(archivePath)}");
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
}
