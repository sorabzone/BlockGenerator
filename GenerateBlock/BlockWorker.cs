using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenerateBlock
{
    public class BlockWorker
    {
        public List<Transaction> inputTransactions;
        private Answer _maxAnswer;
        private readonly int _allowedBlockSize;
        private readonly SemaphoreSlim _semaphore;

        public BlockWorker(int maxSize)
        {
            _allowedBlockSize = maxSize;
            _semaphore = new SemaphoreSlim(1);
            _maxAnswer = new Answer();
        }

        /// <summary>
        /// Runner to generate the block that returns best yield
        /// </summary>
        /// <returns></returns>
        public async Task StartRunner()
        {
            try
            {
                var starttime = DateTime.Now.TimeOfDay;

                if(!ReadTransactions()) return;

                Console.WriteLine($"\n\nRunner in process. Please wait....");

                await BlockFee();

                decimal BTC = Decimal.Add(Convert.ToDecimal(_maxAnswer.TotalFee) / 10000, Convert.ToDecimal(12.5));
                Console.WriteLine("\n\n###################################################################");
                Console.WriteLine($"Start Time:{starttime}");

                Console.WriteLine($"\nBlock size : {_maxAnswer.TotalSize}");
                Console.WriteLine($"Fee : {BTC}");
                PrintBlockTransactions();

                Console.WriteLine($"\n\nEnd Time:{DateTime.Now.TimeOfDay}");
                Console.WriteLine("###################################################################");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during processing. Message: {ex.Message}");
            }
        }

        /// <summary>
        /// This method reads the input file line by line. 
        /// There is option to read lines in parallel, but code is commented because we are working here with only 12 lines
        /// </summary>
        private bool ReadTransactions()
        {
            ConcurrentBag<Transaction> transactionsBag = new ConcurrentBag<Transaction>();

            //Read lines in parallel

            //Parallel.ForEach(File.ReadLines(@".\input.txt", Encoding.UTF8), line =>
            //{
            //    try
            //    {
            //        var transactionDetail = line.Split("\t");

            //        transactionsBag.Add(new Transaction
            //        {
            //            ID = Convert.ToInt32(transactionDetail[0].Trim()),
            //            Size = Convert.ToInt32(transactionDetail[1].Trim()),
            //            Fee = Convert.ToInt16(Convert.ToDouble(transactionDetail[2].Trim()) * 10000)
            //        });

            //        // print the line
            //        Console.WriteLine($"Line: {line}");
            //    }
            //    catch (System.FormatException)
            //    {
            //        Console.WriteLine($"Invalid input, ignore and continue: {line}");
            //    }
            //});

            //Read lines sequentially
            Console.WriteLine($"\n\nInput file contents. \n\n");
            try
            {
                foreach (var line in File.ReadLines(@".\input.txt", Encoding.UTF8))
                {
                    try
                    {
                        //print the line
                        Console.WriteLine($"Line: {line}");

                        var transactionDetail = line.Split("\t");

                        transactionsBag.Add(new Transaction
                        {
                            ID = Convert.ToInt32(transactionDetail[0].Trim()),
                            Size = Convert.ToInt32(transactionDetail[1].Trim()),
                            Fee = Convert.ToInt16(Convert.ToDouble(transactionDetail[2].Trim()) * 10000)
                        });
                    }
                    catch (System.FormatException)
                    {
                        //Console.WriteLine($"Invalid input, ignore and continue: {line}");
                    }
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Program expects 'input.txt' file as input. Please copy 'input.txt' in the project directory and try again.");
                return false;
            }

            inputTransactions = transactionsBag.ToList();
            return true;
        }

        /// <summary>
        /// This method iterates through transactions and calculate the block that yields best result or fee
        /// combinationsDictionary - this objects holds all unique combinations of transactions or all lengths having size less than the aloowed block size
        ///                        - it is dictionary of dictionaries. First dictionary is length or number of transactions in a combination/block
        ///                        - inner dictionary contains all unique combinations of same length as parent dictionary key
        /// Dictionary is used, to reduce search time and maintain unique values
        /// _maxAnswer - This object will hold the result.
        /// Example of combinationsDictionary - {
        ///                                         1: 
        ///                                             {1:{size:10, fee:1},
        ///                                             {2:{size:20, fee:2},
        ///                                             {3:{size:30, fee:3}}
        ///                                         2:
        ///                                             {12:{size:30, fee:3},
        ///                                             {13:{size:40, fee:4},
        ///                                             {23:{size:50, fee:5}}
        ///                                         3:
        ///                                             {123:{size:60, fee:6},
        ///                                     }
        /// </summary>
        /// <returns></returns>
        private async Task BlockFee()
        {
            int size = inputTransactions.Count;
            try
            {
                ConcurrentDictionary<int, ConcurrentDictionary<string, Combination>> combinationsDictionary = new ConcurrentDictionary<int, ConcurrentDictionary<string, Combination>>();
                combinationsDictionary.TryAdd(0, new ConcurrentDictionary<string, Combination>());

                //Iterate through each transactions and analyse all conbinations of all lengths that includes the selected transaction
                for (int used = 0; used < size; used++)
                {
                    var initialCombination = new Combination
                    {
                        TotalSize = inputTransactions[used].Size,
                        TotalFee = inputTransactions[used].Fee,
                    };

                    //Update answer only by taking semaphore because parallel threads may try to update the object at same time
                    if (initialCombination.TotalFee > _maxAnswer.TotalFee)
                    {
                        await _semaphore.WaitAsync();
                        if (initialCombination.TotalFee > _maxAnswer.TotalFee)
                        {
                            _maxAnswer.TotalSize = initialCombination.TotalSize;
                            _maxAnswer.TotalFee = initialCombination.TotalFee;
                            _maxAnswer.Detail = used.ToString();
                        }
                        _semaphore.Release();
                    }
                    combinationsDictionary[0].TryAdd($"-{used.ToString()}-", initialCombination);

                    //This loop iterates to generate combinations of all lengths from size 1 to max length
                    for (int blocklength = 1; blocklength < size; blocklength++)
                    {
                        if (!combinationsDictionary.ContainsKey(blocklength))
                            combinationsDictionary.TryAdd(blocklength, new ConcurrentDictionary<string, Combination>());

                        //This loop iterates through all transactions, and add them to all combinations of length 'n' to generate new combinations of length 'n +1'
                        for (int newT = used + 1; newT < size; newT++)
                        {
                            //Parallel loop analyse the new combination to validate following things
                            // - new combination size is less or equal to allowed block size i.e. 1,000,000 as per test description
                            // - to make sure we are not adding a transaction more than 1 time in a block/combination
                            // - ensure not to generate duplicate or repeated combinations/blocks
                            //   example:- [1,2,3,4], [1,3,4,2], [4,2,3,1], [3,1,2,4] They are all same
                            //   we generate a unique hash to maintain dictionary of each combination
                            //   unique hash, is sorted array od TransactionIDs
                            Parallel.ForEach(combinationsDictionary[blocklength - 1], async block =>
                            {
                                //validating block size and duplicate transactions
                                if ((inputTransactions[newT].Size + block.Value.TotalSize <= _allowedBlockSize) && !block.Key.Contains($"-{newT.ToString()}-"))
                                {
                                    var hash = GetSortedKey(block.Key, newT); //generating unique hash

                                    if (!combinationsDictionary[blocklength].ContainsKey(hash))
                                    {
                                        var newCombination = new Combination
                                        {
                                            TotalSize = inputTransactions[newT].Size + block.Value.TotalSize,
                                            TotalFee = inputTransactions[newT].Fee + block.Value.TotalFee
                                        };

                                        if (!combinationsDictionary[blocklength].ContainsKey(hash))
                                        {
                                            if (newCombination.TotalFee > _maxAnswer.TotalFee)
                                            {
                                                //locking the object and double check the fee, before updating answer
                                                await _semaphore.WaitAsync();
                                                if (newCombination.TotalFee > _maxAnswer.TotalFee)
                                                {
                                                    _maxAnswer.TotalSize = newCombination.TotalSize;
                                                    _maxAnswer.TotalFee = newCombination.TotalFee;
                                                    _maxAnswer.Detail = hash;
                                                }
                                                _semaphore.Release();
                                            }

                                            //adding combination/block to dictionary of combinations
                                            combinationsDictionary[blocklength].TryAdd(hash, newCombination);
                                        }
                                    }
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// This methods inserts the transactionID at correct index to maintain the sorting
        /// and it can used as unique index in dictionary
        /// </summary>
        /// <param name="stringarray"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private string GetSortedKey(string stringarray, int key)
        {
            string result = "";
            bool keyFound = false;
            string[] inputArray = stringarray.Substring(1, stringarray.Length - 2).Split("--");


            for (int i = 0; i < inputArray.Length; i++)
            {
                if (Convert.ToInt32(inputArray[i]) < key || keyFound)
                    result += $"-{inputArray[i]}-";
                else
                {
                    keyFound = true;
                    result += $"-{key}--{inputArray[i]}-";
                }
            }

            if (!keyFound) result += $"-{key}-";

            return result;
        }

        private void PrintBlockTransactions()
        {
            string[] answerArray = _maxAnswer.Detail.Substring(1, _maxAnswer.Detail.Length - 2).Split("--");
            var transactions = answerArray.Select(item =>
            {
                return (Convert.ToInt32(item) + 1).ToString();
            }).ToArray();

            Console.Write($"Transactions in block : [ {string.Join(", ", transactions)} ]");
        }
    }
}