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
    Console.WriteLine("  Split questions:          GeminiChecker <input_file_path> [--split (or -spl)] [--count <int>]");
    Console.WriteLine("  Merge & Analyze folder:   GeminiChecker <input_directory_path>");
    Console.WriteLine("  Verify file with Gemini:  GeminiChecker <input_file_path> --check (or -c) --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("  Generate questions:       GeminiChecker --generate (or -gen) --topics <topics_json_path> --group <index> --count <int> --level <junior/middle> --start-id <int> --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("\nOptions:");
    Console.WriteLine("  -spl, --split             Explicitly trigger split mode on the specified file.");
    Console.WriteLine("  -c, --check               Trigger Gemini verification/correction mode on the specified file.");
    Console.WriteLine("  -gen, --generate          Trigger Gemini question generation mode.");
    Console.WriteLine("  -t, --topics <path>       Path to topics.json file for subject matter matching.");
    Console.WriteLine("  -g, -grp, --group <idx>   Target group index (topic) for generation (default: 0).");
    Console.WriteLine("  -cnt, --count <int>       Number of unique questions per chunk (for split mode) or to generate (default: 8).");
    Console.WriteLine("  -l, --level <string>      Difficulty level (e.g., junior, middle, senior) (default: junior).");
    Console.WriteLine("  -s, --start-id <int>      Starting question_id for newly generated questions (default: 1).");
    Console.WriteLine("  -k, --key <api_key>       Your Google AI Studio API Key (fallback: GEMINI_API_KEY environment variable).");
    Console.WriteLine("  -m, --model <model_name>  Select Gemini model name (default: gemini-3.6-flash).");
    Console.WriteLine("  -p, --prompt <file_path>  Path to an external text file containing custom system prompt rules.");
    return;
}

// ---------------------------------------------------------
// PARAMETERS PARSING LOGIC
// ---------------------------------------------------------
bool isGenerateMode = args.Contains("--generate", StringComparer.OrdinalIgnoreCase) ||
                      args.Contains("-gen", StringComparer.OrdinalIgnoreCase);

bool isCheckMode = args.Contains("--check", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-c", StringComparer.OrdinalIgnoreCase);

bool isSplitMode = args.Contains("--split", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-spl", StringComparer.OrdinalIgnoreCase);

string topicsPath = string.Empty;
int groupIndex = 0;
int count = 8; // Reused as chunkSize in split mode and questionCount in generate mode
string level = "junior";
int startId = 1;
string promptFilePath = string.Empty;
string apiKey = string.Empty;
string modelName = "gemini-3.6-flash";
string inputPath = null;

// Search for topics spec flag
int topicsIndex = Array.FindIndex(args, arg => arg.Equals("--topics", StringComparison.OrdinalIgnoreCase) ||
                                              arg.Equals("-t", StringComparison.OrdinalIgnoreCase));
if (topicsIndex != -1 && topicsIndex + 1 < args.Length)
{
    topicsPath = args[topicsIndex + 1];
}

// Search for group index flag
int groupIndexParsed = Array.FindIndex(args, arg => arg.Equals("--group", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-grp", StringComparison.OrdinalIgnoreCase));
if (groupIndexParsed != -1 && groupIndexParsed + 1 < args.Length)
{
    if (int.TryParse(args[groupIndexParsed + 1], out int gIdx)) groupIndex = gIdx;
}

// Search for count / chunk size flag
int countIndex = Array.FindIndex(args, arg => arg.Equals("--count", StringComparison.OrdinalIgnoreCase) ||
                                             arg.Equals("-cnt", StringComparison.OrdinalIgnoreCase));
if (countIndex != -1 && countIndex + 1 < args.Length)
{
    if (int.TryParse(args[countIndex + 1], out int cnt)) count = cnt;
}

// Search for level flag
int levelIndex = Array.FindIndex(args, arg => arg.Equals("--level", StringComparison.OrdinalIgnoreCase) ||
                                             arg.Equals("-l", StringComparison.OrdinalIgnoreCase));
if (levelIndex != -1 && levelIndex + 1 < args.Length)
{
    level = args[levelIndex + 1];
}

// Search for start ID flag
int startIdIndex = Array.FindIndex(args, arg => arg.Equals("--start-id", StringComparison.OrdinalIgnoreCase) ||
                                               arg.Equals("-s", StringComparison.OrdinalIgnoreCase));
if (startIdIndex != -1 && startIdIndex + 1 < args.Length)
{
    if (int.TryParse(args[startIdIndex + 1], out int sid)) startId = sid;
}

// Search for prompt guidelines flag
int promptIndex = Array.FindIndex(args, arg => arg.Equals("--prompt", StringComparison.OrdinalIgnoreCase) ||
                                              arg.Equals("-p", StringComparison.OrdinalIgnoreCase));
if (promptIndex != -1 && promptIndex + 1 < args.Length)
{
    promptFilePath = args[promptIndex + 1];
}

// Search for key flag
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

// Search for model flag
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
    if (arg.Equals("--generate", StringComparison.OrdinalIgnoreCase) || arg.Equals("-gen", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    if (arg.Equals("--check", StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    if (arg.Equals("--split", StringComparison.OrdinalIgnoreCase) || arg.Equals("-spl", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    if (arg.Equals("--topics", StringComparison.OrdinalIgnoreCase) || arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--group", StringComparison.OrdinalIgnoreCase) || arg.Equals("-g", StringComparison.OrdinalIgnoreCase) || arg.Equals("-grp", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--count", StringComparison.OrdinalIgnoreCase) || arg.Equals("-cnt", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--level", StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--start-id", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--prompt", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
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

if (string.IsNullOrEmpty(inputPath) && !isGenerateMode)
{
    Console.WriteLine("Error: Missing input file or directory path.");
    Environment.Exit(1);
}

// Router to appropriate functionality based on inputs
if (isGenerateMode)
{
    await GenerateQuestionsAsync(topicsPath, groupIndex, count, level, startId, apiKey, modelName, promptFilePath);
}
else if (isCheckMode)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: File '{inputPath}' not found for Gemini verification.");
        Environment.Exit(1);
    }
    await VerifyWithGeminiAsync(inputPath, apiKey, modelName, promptFilePath);
}
else if (isSplitMode)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: Input file '{inputPath}' not found for splitting.");
        Environment.Exit(1);
    }
    await SplitQuestionsAsync(inputPath, count);
}
else
{
    // Backwards-compatible auto-detect router when no explicit split/check/generate flags are passed
    if (Directory.Exists(inputPath))
    {
        await MergeAndAnalyzeAsync(inputPath);
    }
    else if (File.Exists(inputPath))
    {
        await SplitQuestionsAsync(inputPath, count);
    }
    else
    {
        Console.WriteLine($"Error: Path '{inputPath}' is neither a valid file nor directory.");
    }
}

// ==========================================
// 1. GEMINI GENERATION LOGIC
// ==========================================
async Task GenerateQuestionsAsync(string tPath, int gIdx, int qCount, string qLevel, int sId, string apiToken, string selectedModel, string promptPath)
{
    if (string.IsNullOrWhiteSpace(apiToken))
    {
        Console.WriteLine("Error: Gemini API Key is missing. Please provide it using --key / -k option or set GEMINI_API_KEY environment variable.");
        Environment.Exit(1);
    }

    if (string.IsNullOrWhiteSpace(tPath) || !File.Exists(tPath))
    {
        Console.WriteLine($"Error: Topics specification file '{tPath}' not found or not specified.");
        Environment.Exit(1);
    }

    try
    {
        string directory = Path.GetDirectoryName(tPath);
        if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();

        Console.WriteLine("Reading topics.json...");
        string topicsContent = await File.ReadAllTextAsync(tPath);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var topics = JsonSerializer.Deserialize<TopicItem[]>(topicsContent, options);

        if (topics == null || topics.Length == 0)
        {
            Console.WriteLine("Error: Failed to deserialize topics or file is empty.");
            Environment.Exit(1);
        }

        // Retrieve localized topic names for the requested group
        var groupTopics = topics.Where(t => t.GroupIndex == gIdx).ToList();
        if (groupTopics.Count == 0)
        {
            Console.WriteLine($"Error: No topic names found in topics.json for group index {gIdx}.");
            Environment.Exit(1);
        }

        string topicGuidelines = string.Join("\n", groupTopics.Select(t => $"- Language '{t.Lang}': Topic name is \"{t.Name}\""));

        // ---------------------------------------------------------
        // DYNAMIC DUPLICATION PROTECTION (BLACKLISTING)
        // ---------------------------------------------------------
        Console.WriteLine("Scanning directory for existing questions to compile blacklist...");
        var regex = new Regex(@"^.+_\d+_\d+(_chn)?\.json$", RegexOptions.IgnoreCase);
        var jsonFiles = Directory.GetFiles(directory, "*.json")
            .Where(f => regex.IsMatch(Path.GetFileName(f)))
            .ToList();

        var existingQuestions = new List<string>();
        foreach (var file in jsonFiles)
        {
            try
            {
                string content = await File.ReadAllTextAsync(file);
                var questions = JsonSerializer.Deserialize<QuestionItem[]>(content, options);
                if (questions != null)
                {
                    // Extract English questions to use as semantic exclude filters for Gemini
                    var filtered = questions.Where(q => q.GroupIndex == gIdx && string.Equals(q.Lang, "en", StringComparison.OrdinalIgnoreCase));
                    foreach (var q in filtered)
                    {
                        if (!string.IsNullOrWhiteSpace(q.Question))
                        {
                            existingQuestions.Add(q.Question.Trim());
                        }
                    }
                }
            }
            catch { /* Ignore corrupted or unrelated json files */ }
        }

        string blacklistPrompt = string.Empty;
        if (existingQuestions.Count > 0)
        {
            Console.WriteLine($"Found {existingQuestions.Count} already existing unique questions. Registering them as a blacklist...");
            blacklistPrompt = "\nCRITICAL: You MUST NOT generate questions that are identical or semantically similar to any of the following already existing questions in our database:\n" +
                              string.Join("\n", existingQuestions.Select((q, index) => $"{index + 1}. {q}"));
        }
        else
        {
            Console.WriteLine("No pre-existing questions found. Generating from a clean slate.");
        }

        // ---------------------------------------------------------
        // PROMPT FORMULATION
        // ---------------------------------------------------------
        string systemPrompt;
        if (!string.IsNullOrWhiteSpace(promptPath))
        {
            if (!File.Exists(promptPath))
            {
                Console.WriteLine($"Error: Prompt rules file '{promptPath}' not found.");
                Environment.Exit(1);
            }
            Console.WriteLine($"Loading generation guidelines from: {promptPath}...");
            systemPrompt = await File.ReadAllTextAsync(promptPath);
        }
        else
        {
            Console.WriteLine("No custom generation guidelines specified. Using default educator guidelines...");
            systemPrompt = "You are an expert developer, educator, and technical quiz creator.";
        }

        string generationInstruction =
            $"Your task is to generate exactly {qCount} unique multiple-choice quiz questions for group index {gIdx}.\n" +
            $"Difficulty level: '{qLevel}'.\n" +
            $"Start indexing with question_id: {sId}.\n\n" +
            $"For EACH unique question_id, you must generate exactly 5 translations (one for each language: en, uk, de, es, fr).\n" +
            $"The questions must be perfectly aligned with the topics defined for this group in each language:\n" +
            $"{topicGuidelines}\n\n" +
            $"Requirements for each question object:\n" +
            $"1. 'question_id' must be the same integer across all 5 language translations.\n" +
            $"2. 'lang' must be exactly 'en', 'uk', 'de', 'es', or 'fr'.\n" +
            $"3. 'level' must be exactly '{qLevel}'.\n" +
            $"4. 'group_index' must be exactly {gIdx}.\n" +
            $"5. 'question' should be clear, technical, and accurate. Code snippets can be used where relevant.\n" +
            $"6. 'answer_a', 'answer_b', 'answer_c', 'answer_d' must contain the choices.\n" +
            $"7. 'answer_win' must be exactly 'a', 'b', 'c', or 'd'. It must be identical across all 5 translations for that question_id.\n" +
            $"8. 'explanation' must explain why the winning answer is correct.\n\n" +
            $"Output ONLY a valid JSON array of QuestionItem objects. Do not write any explanations, greetings, introduction, or markdown block wrapping (like ```json). Just the raw JSON content." +
            blacklistPrompt;

        string fullPrompt = $"{systemPrompt}\n\n{generationInstruction}";

        Console.WriteLine($"Connecting to Gemini API using model: {selectedModel}...");
        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);

        Console.WriteLine("Generating questions via Gemini. Please wait...");
        var response = await model.GenerateContent(fullPrompt);

        if (response == null || string.IsNullOrWhiteSpace(response.Text))
        {
            Console.WriteLine("Error: Gemini returned an empty response.");
            Environment.Exit(1);
        }

        string processedJson = response.Text.Trim();

        // Clean up markdown block wrapping
        if (processedJson.StartsWith("```"))
        {
            int firstLineEnd = processedJson.IndexOf('\n');
            if (firstLineEnd != -1)
            {
                processedJson = processedJson.Substring(firstLineEnd).Trim();
            }
            if (processedJson.EndsWith("```"))
            {
                processedJson = processedJson.Substring(0, processedJson.Length - 3).Trim();
            }
        }

        // Verify JSON before saving
        try
        {
            var generatedQuestions = JsonSerializer.Deserialize<QuestionItem[]>(processedJson, options);
            if (generatedQuestions == null || generatedQuestions.Length == 0)
            {
                Console.WriteLine("Error: Deserialized object array is empty.");
                Environment.Exit(1);
            }

            int width = Math.Max(4, sId.ToString().Length);
            string paddedStartIndex = sId.ToString("D" + width);

            string outputFileName = $"questions_{gIdx}_{paddedStartIndex}.json";
            string outputPath = Path.Combine(directory, outputFileName);

            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string formattedJson = JsonSerializer.Serialize(generatedQuestions, writeOptions);
            await File.WriteAllTextAsync(outputPath, formattedJson);

            Console.WriteLine($"\nSuccess! Generated {generatedQuestions.Length} records ({generatedQuestions.Length / 5} unique questions).");
            Console.WriteLine($"Output file created: {outputPath}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine("Error: Response from Gemini is not a valid JSON array. Details: " + ex.Message);
            Console.WriteLine("Raw response was:");
            Console.WriteLine(processedJson);
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred during generation: {ex.Message}");
        Environment.Exit(1);
    }
}

// ==========================================
// 2. GEMINI VERIFICATION LOGIC
// ==========================================
async Task VerifyWithGeminiAsync(string filePath, string apiToken, string selectedModel, string promptPath)
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

        string systemPrompt;

        // Load custom prompt if file path is provided, otherwise fallback to default Java prompt
        if (!string.IsNullOrWhiteSpace(promptPath))
        {
            if (!File.Exists(promptPath))
            {
                Console.WriteLine($"Error: Prompt file '{promptPath}' not found.");
                Environment.Exit(1);
            }
            Console.WriteLine($"Loading system prompt rules from: {promptPath}...");
            systemPrompt = await File.ReadAllTextAsync(promptPath);
        }
        else
        {
            Console.WriteLine("No custom prompt specified. Using default Java proofreader prompt...");
            systemPrompt =
                "You are an expert Java developer and proofreader. " +
                "Your task is to review the following JSON array of quiz questions. " +
                "1. Check for spelling, grammar, punctuation, and clear phrasing in all languages.\n" +
                "2. Check for technical correctness (OOP, collections, syntax, JVM) of Java code inside questions/answers/explanations.\n" +
                "3. Correct any errors you find.\n" +
                "4. Output ONLY the updated JSON array. Do not write any explanations, greetings, introduction, or markdown block wrapping (like ```json). Just the raw JSON content.";
        }

        Console.WriteLine($"Connecting to Gemini API using model: {selectedModel}...");
        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);

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
// 3. MERGE AND ANALYZE LOGIC
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
// 4. SPLIT LOGIC
// ==========================================
async Task SplitQuestionsAsync(string filePath, int chunkSize)
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

        Console.WriteLine($"Splitting data by 'group_index' and grouping by ID ranges [1-{chunkSize}], [{chunkSize + 1}-{chunkSize * 2}]...");

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var group in groups)
        {
            var groupQuestions = group.ToList();

            // Group questions of this group by their ID range blocks dynamically using chunkSize
            var idBlocks = groupQuestions
                .GroupBy(q => (q.QuestionId - 1) / chunkSize)
                .OrderBy(g => g.Key);

            foreach (var idBlock in idBlocks)
            {
                int blockKey = idBlock.Key;

                int startIndex = (blockKey * chunkSize) + 1;

                // Format startIndex with the dynamically calculated padding width, e.g. "D3" for 3 digits
                string paddedStartIndex = startIndex.ToString("D" + width);

                string groupFileName = $"{fileNameWithoutExt}_{group.Key}_{paddedStartIndex}{extension}";
                string groupFilePath = Path.Combine(directory, groupFileName);

                var blockList = idBlock.ToList();
                string outputJson = JsonSerializer.Serialize(blockList, writeOptions);
                await File.WriteAllTextAsync(groupFilePath, outputJson);

                Console.WriteLine($"Created file: {groupFilePath} ({blockList.Count} records, ID range: {startIndex} to {startIndex + chunkSize - 1})");
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

// Data models for JSON mapping
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

public class TopicItem
{
    [JsonPropertyName("group_index")]
    public int GroupIndex { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}