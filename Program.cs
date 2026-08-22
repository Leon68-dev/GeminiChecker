using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mscc.GenerativeAI;

if (args.Length < 1)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Split questions:          GeminiChecker <input_file_path>");
    Console.WriteLine("  Merge & Analyze folder:   GeminiChecker <input_directory_path>");
    Console.WriteLine("  Verify file with Gemini:  GeminiChecker <input_file_path> --check (or -c) --key <api_key> [--model <model_name>]");
    Console.WriteLine("\nOptions:");
    Console.WriteLine("  -c, --check               Trigger Gemini verification/correction mode on the specified file.");
    Console.WriteLine("  -k, --key <api_key>       Your Google AI Studio API Key (fallback: GEMINI_API_KEY environment variable).");
    Console.WriteLine("  -m, --model <model_name>  Select Gemini model name (default: gemini-3.6-flash).");
    return;
}

// ---------------------------------------------------------
// PARAMETERS PARSING LOGIC
// ---------------------------------------------------------
bool isCheckMode = args.Contains("--check", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-c", StringComparer.OrdinalIgnoreCase);

string apiKey = string.Empty;
string modelName = "gemini-3.6-flash"; // Default model
string inputPath = null;

// Search for key and model flags
int keyIndex = Array.FindIndex(args, arg => arg.Equals("--key", StringComparison.OrdinalIgnoreCase) ||
                                           arg.Equals("-k", StringComparison.OrdinalIgnoreCase));
if (keyIndex != -1 && keyIndex + 1 < args.Length)
{
    apiKey = args[keyIndex + 1];
}
else
{
    // Try to fallback to environment variable for convenience
    apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
}

int modelIndex = Array.FindIndex(args, arg => arg.Equals("--model", StringComparison.OrdinalIgnoreCase) ||
                                             arg.Equals("-m", StringComparison.OrdinalIgnoreCase));
if (modelIndex != -1 && modelIndex + 1 < args.Length)
{
    modelName = args[modelIndex + 1];
}

// Extract the input file or directory path (skip flags and their values)
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg.Equals("--check", StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    if (arg.Equals("--key", StringComparison.OrdinalIgnoreCase) || arg.Equals("-k", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--model", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }

    inputPath = arg;
    break;
}

if (string.IsNullOrEmpty(inputPath))
{
    Console.WriteLine("Error: Missing input file or directory path.");
    Environment.Exit(1);
}

// Router to appropriate functionality based on inputs
if (isCheckMode)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: File '{inputPath}' not found for Gemini verification.");
        Environment.Exit(1);
    }
    await VerifyWithGeminiAsync(inputPath, apiKey, modelName);
}
else
{
    if (Directory.Exists(inputPath))
    {
        await MergeAndAnalyzeAsync(inputPath);
    }
    else if (File.Exists(inputPath))
    {
        await SplitQuestionsAsync(inputPath);
    }
    else
    {
        Console.WriteLine($"Error: Path '{inputPath}' is neither a valid file nor directory.");
    }
}

// ==========================================
// 1. GEMINI VERIFICATION LOGIC
// ==========================================
async Task VerifyWithGeminiAsync(string filePath, string apiToken, string selectedModel)
{
    if (string.IsNullOrWhiteSpace(apiToken))
    {
        Console.WriteLine("Error: Gemini API Key is missing. Please provide it using --key / -k option or set GEMINI_API_KEY environment variable.");
        Environment.Exit(1);
    }

    try
    {
        Console.WriteLine($"Reading file: {filePath}...");
        string originalJson = await File.ReadAllTextAsync(filePath);

        Console.WriteLine($"Connecting to Gemini API using model: {selectedModel}...");
        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);

        // Strict prompt to ensure Gemini behaves as a JSON parser/corrector
        string systemPrompt =
            "You are an expert Java developer and proofreader. " +
            "Your task is to review the following JSON array of quiz questions. " +
            "1. Check for spelling, grammar, punctuation, and clear phrasing in all languages.\n" +
            "2. Check for technical correctness (OOP, collections, syntax, JVM) of Java code inside questions/answers/explanations.\n" +
            "3. Correct any errors you find.\n" +
            "4. Output ONLY the updated JSON array. Do not write any explanations, greetings, introduction, or markdown block wrapping (like ```json). Just the raw JSON content.";

        string fullPrompt = $"{systemPrompt}\n\nHere is the JSON to check:\n{originalJson}";

        Console.WriteLine("Sending data to Gemini for review. Please wait...");
        var response = await model.GenerateContent(fullPrompt);

        if (response == null || string.IsNullOrWhiteSpace(response.Text))
        {
            Console.WriteLine("Error: Gemini returned an empty response or the request was blocked.");
            Environment.Exit(1); // Exit the program on empty response error
        }

        string processedJson = response.Text.Trim();

        // Clean up markdown code blocks if Gemini wraps it in ```json ... ``` anyway
        if (processedJson.StartsWith("```"))
        {
            // Remove starting ```json or ```
            int firstLineEnd = processedJson.IndexOf('\n');
            if (firstLineEnd != -1)
            {
                processedJson = processedJson.Substring(firstLineEnd).Trim();
            }
            // Remove ending ```
            if (processedJson.EndsWith("```"))
            {
                processedJson = processedJson.Substring(0, processedJson.Length - 3).Trim();
            }
        }

        // Verify if it is a valid JSON before saving
        try
        {
            using var doc = JsonDocument.Parse(processedJson);
        }
        catch (JsonException)
        {
            Console.WriteLine("Error: Response from Gemini is not a valid JSON. Response text was:");
            Console.WriteLine(processedJson);
            Environment.Exit(1); // Exit the program on JSON validation error
        }

        // Check if any changes were made
        if (originalJson.Trim() == processedJson)
        {
            Console.WriteLine("No changes needed. The file is already perfect!");
        }
        else
        {
            // Define path for the new file, e.g. questions_0_1_chn.json
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            string outputFileName = $"{fileNameWithoutExt}_chn{extension}";
            string outputPath = Path.Combine(directory, outputFileName);

            await File.WriteAllTextAsync(outputPath, processedJson);
            Console.WriteLine($"\nSuccess! Corrections detected.");
            Console.WriteLine($"Original file: {filePath}");
            Console.WriteLine($"Corrected file saved to: {outputPath}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred: {ex.Message}");
        Environment.Exit(1); // Exit the program on any API or connection exception
    }
}

// ==========================================
// 2. MERGE AND ANALYZE LOGIC
// ==========================================
async Task MergeAndAnalyzeAsync(string directoryPath)
{
    try
    {
        Console.WriteLine($"Analyzing directory: {directoryPath}...");

        // Regex to match only chunk files like questions_0_1.json or questions_0_1_chn.json
        // This safely ignores main questions.json and previous timestamped merged files
        var regex = new Regex(@"^.+_\d+_\d+(_chn)?\.json$", RegexOptions.IgnoreCase);

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json")
            .Where(f => regex.IsMatch(Path.GetFileName(f)))
            .ToList();

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("No chunk files matching the pattern (e.g. *_[group]_[start].json) found.");
            return;
        }

        // Apply '_chn' override logic: ignore original if '_chn' version exists
        var filesToProcess = new List<string>();
        foreach (var file in jsonFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.EndsWith("_chn", StringComparison.OrdinalIgnoreCase))
            {
                filesToProcess.Add(file);
            }
            else
            {
                string chnFile = Path.Combine(directoryPath, fileName + "_chn.json");
                // If there is no corresponding _chn file in the list, process the original
                if (!jsonFiles.Any(f => string.Equals(f, chnFile, StringComparison.OrdinalIgnoreCase)))
                {
                    filesToProcess.Add(file);
                }
            }
        }

        Console.WriteLine($"Found {jsonFiles.Count} total chunks. After applying '_chn' overrides, processing {filesToProcess.Count} files:");
        foreach (var file in filesToProcess)
        {
            Console.WriteLine($"  - {Path.GetFileName(file)}");
        }

        var allQuestions = new List<QuestionItem>();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Read and deserialize each selected file
        foreach (var file in filesToProcess)
        {
            string content = await File.ReadAllTextAsync(file);
            var questions = JsonSerializer.Deserialize<QuestionItem[]>(content, options);
            if (questions != null)
            {
                allQuestions.AddRange(questions);
            }
        }

        if (allQuestions.Count == 0)
        {
            Console.WriteLine("No questions retrieved from files.");
            return;
        }

        // De-duplicate by unique key to prevent overlaps, then sort by group_index and question_id
        var orderedQuestions = allQuestions
            .GroupBy(q => new { q.GroupIndex, q.QuestionId, Lang = q.Lang.ToLower().Trim() })
            .Select(g => g.First())
            .OrderBy(q => q.GroupIndex)
            .ThenBy(q => q.QuestionId)
            .ToList();

        // Create output filename with timestamp
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string outputFileName = $"questions_{timestamp}.json";
        string outputFilePath = Path.Combine(directoryPath, outputFileName);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string outputJson = JsonSerializer.Serialize(orderedQuestions, writeOptions);
        await File.WriteAllTextAsync(outputFilePath, outputJson);

        Console.WriteLine($"\nSuccessfully merged all files into: {outputFilePath}");

        // Display Statistics at the end
        Console.WriteLine("\n================ STATISTICS ================");

        int totalUniqueQuestions = orderedQuestions.Select(q => q.QuestionId).Distinct().Count();
        Console.WriteLine($"усього {totalUniqueQuestions} питань");

        var groups = orderedQuestions.GroupBy(q => q.GroupIndex).OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            int groupUniqueQuestions = group.Select(q => q.QuestionId).Distinct().Count();
            Console.WriteLine($"група {group.Key}: {groupUniqueQuestions} питань усього");

            var langs = group.Select(q => q.Lang.ToLower().Trim()).Distinct().OrderBy(l => l);
            foreach (var lang in langs)
            {
                int langCount = group.Count(q => q.Lang.Equals(lang, StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"група {group.Key}: {langCount} питань {lang}");
            }
        }
        Console.WriteLine("============================================");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred during merging: {ex.Message}");
    }
}

// ==========================================
// 3. SPLIT LOGIC
// ==========================================
async Task SplitQuestionsAsync(string filePath)
{
    try
    {
        Console.WriteLine($"Reading data from file {filePath}...");
        string jsonContent = await File.ReadAllTextAsync(filePath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var questions = JsonSerializer.Deserialize<QuestionItem[]>(jsonContent, options);

        if (questions == null || questions.Length == 0)
        {
            Console.WriteLine("Error: Failed to deserialize data or the file is empty.");
            return;
        }

        Console.WriteLine($"Total records read: {questions.Length}");

        // Find the maximum question ID in the entire file
        int maxId = questions.Max(q => q.QuestionId);

        // Calculate the padding width based on the number of digits in the maximum ID
        int width = maxId.ToString().Length;

        Console.WriteLine($"Maximum Question ID: {maxId}. Filename index padding width: {width}");

        // Group all data by group_index
        var groups = questions.GroupBy(q => q.GroupIndex).OrderBy(g => g.Key);

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath).ToLower();
        string extension = Path.GetExtension(filePath).ToLower();

        Console.WriteLine("Splitting data by 'group_index' and grouping by ID ranges [1-8], [9-16]...");

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var group in groups)
        {
            var groupQuestions = group.ToList();

            // Group questions of this group by their ID range blocks
            var idBlocks = groupQuestions
                .GroupBy(q => (q.QuestionId - 1) / 8)
                .OrderBy(g => g.Key);

            foreach (var idBlock in idBlocks)
            {
                int blockKey = idBlock.Key;

                int startIndex = (blockKey * 8) + 1;

                // Format startIndex with the dynamically calculated padding width, e.g. "D3" for 3 digits
                string paddedStartIndex = startIndex.ToString("D" + width);

                string groupFileName = $"{fileNameWithoutExt}_{group.Key}_{paddedStartIndex}{extension}";
                string groupFilePath = Path.Combine(directory, groupFileName);

                var blockList = idBlock.ToList();
                string outputJson = JsonSerializer.Serialize(blockList, writeOptions);
                await File.WriteAllTextAsync(groupFilePath, outputJson);

                Console.WriteLine($"Created file: {groupFilePath} ({blockList.Count} records, ID range: {startIndex} to {startIndex + 7})");
            }
        }

        Console.WriteLine("Success! All groups processed and saved strictly by ID ranges.");
    }
    catch (JsonException jsonEx)
    {
        Console.WriteLine($"JSON processing error: {jsonEx.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An unexpected error occurred: {ex.Message}");
    }
}

// Data model for JSON mapping
public class QuestionItem
{
    [JsonPropertyName("question_id")]
    public int QuestionId { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("answer_a")]
    public string AnswerA { get; set; } = string.Empty;

    [JsonPropertyName("answer_b")]
    public string AnswerB { get; set; } = string.Empty;

    [JsonPropertyName("answer_c")]
    public string AnswerC { get; set; } = string.Empty;

    [JsonPropertyName("answer_d")]
    public string AnswerD { get; set; } = string.Empty;

    [JsonPropertyName("answer_win")]
    public string AnswerWin { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("group_index")]
    public int GroupIndex { get; set; }
}