using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

if (args.Length < 1)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  View statistics (file):   GeminiChecker <input_file_path> --stats (or --stat, -st, -a, --analyze)");
    Console.WriteLine("  Split questions:          GeminiChecker <input_file_path> [--split (or -spl)] [--count <int>]");
    Console.WriteLine("  Merge & Analyze folder:   GeminiChecker <input_directory_path>");
    Console.WriteLine("  Verify file with Gemini:  GeminiChecker <input_file_path> --check (or -c) --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("  Generate questions:       GeminiChecker --generate (or -gen) --topics <topics_json_path> --group <index> --count <int> --level <junior/middle> --start-id <int> --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("  Export prompt to file:    GeminiChecker --save-prompt (or -sp, --dump-prompt, -dp) --topics <topics_json_path> --group <index> --count <int> --level <junior/middle> --start-id <int> [--prompt <prompt_file_path>]");
    Console.WriteLine("\nOptions:");
    Console.WriteLine("  -st, --stat, --stats      Show detailed question statistics (totals, per group, per language).");
    Console.WriteLine("  -a, --analyze             Alias for statistics and JSON validation.");
    Console.WriteLine("  -spl, --split             Explicitly trigger split mode on the specified file.");
    Console.WriteLine("  -c, --check               Trigger Gemini verification/correction mode on the specified file.");
    Console.WriteLine("  -gen, --generate          Trigger Gemini question generation mode.");
    Console.WriteLine("  -sp, --save-prompt        Assemble and save the complete generation prompt to a text file without calling API.");
    Console.WriteLine("  -dp, --dump-prompt        Alias for --save-prompt.");
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

bool isSavePromptMode = args.Contains("--save-prompt", StringComparer.OrdinalIgnoreCase) ||
                        args.Contains("-sp", StringComparer.OrdinalIgnoreCase) ||
                        args.Contains("--dump-prompt", StringComparer.OrdinalIgnoreCase) ||
                        args.Contains("-dp", StringComparer.OrdinalIgnoreCase);

bool isCheckMode = args.Contains("--check", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-c", StringComparer.OrdinalIgnoreCase);

bool isSplitMode = args.Contains("--split", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-spl", StringComparer.OrdinalIgnoreCase);

bool isAnalyzeMode = args.Contains("--analyze", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("-a", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("--stats", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("--stat", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("-st", StringComparer.OrdinalIgnoreCase);

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
    // Fallback to environment variable for convenience
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
    if (arg.Equals("--save-prompt", StringComparison.OrdinalIgnoreCase) || arg.Equals("-sp", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--dump-prompt", StringComparison.OrdinalIgnoreCase) || arg.Equals("-dp", StringComparison.OrdinalIgnoreCase))
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
    if (arg.Equals("--analyze", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-a", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--stats", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--stat", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-st", StringComparison.OrdinalIgnoreCase))
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

if (string.IsNullOrEmpty(inputPath) && !isGenerateMode && !isSavePromptMode)
{
    Console.WriteLine("Error: Missing input file or directory path.");
    Environment.Exit(1);
}

// Router to appropriate functionality based on inputs
if (isSavePromptMode)
{
    await SavePromptToFileAsync(topicsPath, groupIndex, count, level, startId, promptFilePath);
}
else if (isGenerateMode)
{
    await GenerateQuestionsAsync(topicsPath, groupIndex, count, level, startId, apiKey, modelName, promptFilePath);
}
else if (isAnalyzeMode)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: File '{inputPath}' not found for analysis.");
        Environment.Exit(1);
    }
    await AnalyzeJsonFileAsync(inputPath);
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
    // Backwards-compatible auto-detect router when no explicit flags are passed
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
// 1. ASSEMBLE PROMPT HELPER
// ==========================================
async Task<(string FullPrompt, string OutputDirectory)> BuildPromptAsync(string tPath, int gIdx, int qCount, string qLevel, int sId, string promptPath)
{
    if (string.IsNullOrWhiteSpace(tPath) || !File.Exists(tPath))
    {
        Console.WriteLine($"Error: Topics specification file '{tPath}' not found or not specified.");
        Environment.Exit(1);
    }

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
        systemPrompt = "You are an expert educator, scholar, and quiz creator.";
    }

    int endId = sId + qCount - 1;

    string generationInstruction =
        $"TASK SPECIFICATION:\n" +
        $"- Target Group Index: {gIdx}\n" +
        $"- Question ID Range : {sId} to {endId} (Total: {qCount} unique questions)\n" +
        $"- Difficulty Level  : '{qLevel}'\n\n" +
        $"CORE SOURCING AND STRUCTURAL RULES:\n" +
        $"- STRICT QUESTION UNIQUENESS: Every question MUST be 100% distinct in concept, scenario, code snippet, and problem statement. Absolutely NO duplicate questions, minor rephrasings, or semantic repetitions across different question_ids, especially within the same group index ({gIdx}) and difficulty level ('{qLevel}'). Each question_id must test a completely different aspect or subtopic.\n" +
        $"- Source questions and conceptual depth from reputable US and European technical websites, literature, and official standards.\n" +
        $"- Correct and incorrect answer choices must be of comparable length and complexity so that the correct answer is not obvious.\n" +
        $"- Explanations must be approximately 5 sentences long, clearly detailing why the correct answer is right and why distractors are wrong.\n" +
        $"- ABSOLUTE PROHIBITION ON BACKTICKS: NEVER use the backtick symbol (`) anywhere in the text. For code elements, function names, types, and keywords (like 'std::vector', 'push_back', 'const', 'int'), ALWAYS use single quotes ('...') or double quotes (\"...\").\n" +
        $"- The correct answer keys MUST follow a simple rotating cycle across consecutive question_ids: a, b, c, d, a, b, c, d...\n\n" +
        $"For EACH unique question_id, you must generate exactly 5 translations (one for each language: en, uk, de, es, fr).\n" +
        $"The questions must be perfectly aligned with the topics defined for this group in each language:\n" +
        $"{topicGuidelines}\n\n" +
        $"Requirements for each question object:\n" +
        $"1. 'question_id' must be the same integer across all 5 language translations.\n" +
        $"2. 'lang' must be exactly 'en', 'uk', 'de', 'es', or 'fr'.\n" +
        $"3. 'level' must be exactly '{qLevel}'.\n" +
        $"4. 'group_index' must be exactly {gIdx}.\n" +
        $"5. 'question' must be unique, clear, factual, and accurate. Use domain-specific terminology, formulas, code snippets, or relevant examples without repeating concepts tested in other question_ids.\n" +
        $"6. 'answer_a', 'answer_b', 'answer_c', 'answer_d' must contain the choices.\n" +
        $"7. 'answer_win' must be exactly 'a', 'b', 'c', or 'd'. It MUST be identical across all 5 translations for that question_id and follow the cyclical sequence (a, b, c, d, a, b, c, d...).\n" +
        $"8. 'explanation' must be approximately 5 sentences long explaining the reasoning.\n\n" +
        blacklistPrompt;

    string fullPrompt = $"{systemPrompt}\n\n{generationInstruction}";
    return (fullPrompt, directory);
}

// ==========================================
// 2. SAVE PROMPT TO FILE LOGIC
// ==========================================
async Task SavePromptToFileAsync(string tPath, int gIdx, int qCount, string qLevel, int sId, string promptPath)
{
    int endId = sId + qCount - 1;
    Console.WriteLine("============================================");
    Console.WriteLine($"Exporting Prompt Target:");
    Console.WriteLine($"  Group Index : {gIdx}");
    Console.WriteLine($"  ID Range    : {sId} - {endId} (Total: {qCount} unique questions)");
    Console.WriteLine($"  Difficulty  : {qLevel}");
    Console.WriteLine("============================================");

    try
    {
        var (fullPrompt, directory) = await BuildPromptAsync(tPath, gIdx, qCount, qLevel, sId, promptPath);

        int width = Math.Max(4, sId.ToString().Length);
        string paddedStartIndex = sId.ToString("D" + width);

        string outputFileName = $"prompt_{gIdx}_{paddedStartIndex}.txt";
        string outputPath = Path.Combine(directory, outputFileName);

        await File.WriteAllTextAsync(outputPath, fullPrompt);

        Console.WriteLine($"\n[SUCCESS] Prompt assembled and exported successfully!");
        Console.WriteLine($"Prompt file saved to: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while exporting prompt: {ex.Message}");
        Environment.Exit(1);
    }
}

// ==========================================
// 3. GEMINI GENERATION LOGIC
// ==========================================
async Task GenerateQuestionsAsync(string tPath, int gIdx, int qCount, string qLevel, int sId, string apiToken, string selectedModel, string promptPath)
{
    if (string.IsNullOrWhiteSpace(apiToken))
    {
        Console.WriteLine("Error: Gemini API Key is missing. Please provide it using --key / -k option or set GEMINI_API_KEY environment variable.");
        Environment.Exit(1);
    }

    int endId = sId + qCount - 1;
    Console.WriteLine("============================================");
    Console.WriteLine($"Generation Target:");
    Console.WriteLine($"  Group Index : {gIdx}");
    Console.WriteLine($"  ID Range    : {sId} - {endId} (Total: {qCount} unique questions)");
    Console.WriteLine($"  Difficulty  : {qLevel}");
    Console.WriteLine("============================================");

    string directory = Directory.GetCurrentDirectory();
    int width = Math.Max(4, sId.ToString().Length);
    string paddedStartIndex = sId.ToString("D" + width);
    string baseFileName = $"questions_{gIdx}_{paddedStartIndex}";
    string rawTextReceived = string.Empty;

    try
    {
        var promptResult = await BuildPromptAsync(tPath, gIdx, qCount, qLevel, sId, promptPath);
        string fullPrompt = promptResult.FullPrompt;
        directory = promptResult.OutputDirectory;

        Console.WriteLine($"Connecting to Gemini API using model: {selectedModel}...");
        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);

        // Configure generation parameters to unlock the maximum output capacity of 65,536 tokens
        var generationConfig = new GenerationConfig
        {
            MaxOutputTokens = 65536
        };

        // Start request and track elapsed execution time
        Console.WriteLine("Generating questions via Gemini. Please wait...");
        var stopwatch = Stopwatch.StartNew();
        var generateTask = model.GenerateContent(fullPrompt, generationConfig: generationConfig);

        Console.Write("Elapsed: 00:00");
        
        while (!generateTask.IsCompleted)
        {
            await Task.WhenAny(generateTask, Task.Delay(1000));
            if (!generateTask.IsCompleted)
            {
                Console.Write($"\rElapsed: {stopwatch.Elapsed:mm\\:ss}   ");
            }
        }

        stopwatch.Stop();
        Console.WriteLine($"\rCompleted in: {stopwatch.Elapsed:mm\\:ss}!      ");

        var response = await generateTask;

        if (response == null || string.IsNullOrWhiteSpace(response.Text))
        {
            string emptyErrorPath = Path.Combine(directory, $"{baseFileName}_error.txt");
            await File.WriteAllTextAsync(emptyErrorPath, "Gemini returned an empty response or null.");
            Console.WriteLine($"\n[ERROR] Gemini returned an empty response.");
            Console.WriteLine($"Error log saved to: {emptyErrorPath}");
            Environment.Exit(1);
        }

        rawTextReceived = response.Text;
        string processedJson = rawTextReceived.Trim();

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
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var generatedQuestions = JsonSerializer.Deserialize<QuestionItem[]>(processedJson, options);
            if (generatedQuestions == null || generatedQuestions.Length == 0)
            {
                string emptyArrayPath = Path.Combine(directory, $"{baseFileName}_raw_error.txt");
                await File.WriteAllTextAsync(emptyArrayPath, rawTextReceived);
                Console.WriteLine($"\n[ERROR] Deserialized object array is empty.");
                Console.WriteLine($"Raw response from API saved to: {emptyArrayPath}");
                Environment.Exit(1);
            }

            string outputFileName = $"{baseFileName}.json";
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
            // Save the raw text received from Gemini to disk so work is not lost
            string errorOutputPath = Path.Combine(directory, $"{baseFileName}_raw_error.txt");
            await File.WriteAllTextAsync(errorOutputPath, rawTextReceived);

            Console.WriteLine("\n[ERROR] Response from Gemini is not a valid JSON array.");
            Console.WriteLine($"Details: {ex.Message}");
            Console.WriteLine($"Raw response from API saved to: {errorOutputPath}");
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(rawTextReceived))
        {
            string errorPath = Path.Combine(directory, $"{baseFileName}_raw_error.txt");
            try
            {
                await File.WriteAllTextAsync(errorPath, rawTextReceived);
                Console.WriteLine($"Raw API response was saved to: {errorPath}");
            }
            catch { /* Ignore secondary disk write exceptions */ }
        }

        Console.WriteLine($"\n[ERROR] An error occurred during generation: {ex.Message}");
        Environment.Exit(1);
    }
}

// ==========================================
// 4. GEMINI VERIFICATION LOGIC
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

        // Strict non-negotiable format rules that ensure Gemini never breaks the output JSON structure
        string strictFormatRules =
            "CRITICAL INSTRUCTIONS FOR OUTPUT FORMAT:\n" +
            "1. You MUST return ALL questions and ALL translations from the input. Do NOT abbreviate, truncate, or omit any questions, languages, or fields.\n" +
            "2. The output JSON array must contain the exact same number of items as the input, with all fields preserved and only corrected where necessary.\n" +
            "3. Output ONLY the updated JSON array. Do NOT write any explanations, conversational filler, greetings, introductions, or markdown block wrapping (like ```json). Return just the raw JSON content.";

        string systemPrompt;

        // Load custom prompt if file path is provided, otherwise fallback to default proofreader prompt
        if (!string.IsNullOrWhiteSpace(promptPath))
        {
            if (!File.Exists(promptPath))
            {
                Console.WriteLine($"Error: Prompt file '{promptPath}' not found.");
                Environment.Exit(1);
            }
            Console.WriteLine($"Loading system prompt rules from: {promptPath}...");
            string customRules = await File.ReadAllTextAsync(promptPath);

            // Integrate custom domain rules with the strict formatting requirements
            systemPrompt =
                "You are an expert educator, researcher, and proofreader.\n" +
                "Evaluate the questions using the following specific criteria and subject matter rules:\n" +
                $"{customRules}\n\n" +
                "Additionally, check for spelling, grammar, punctuation, and clear phrasing in all languages.\n\n" +
                strictFormatRules;
        }
        else
        {
            Console.WriteLine("No custom prompt specified. Using default proofreader prompt...");
            systemPrompt =
                "You are an expert educator, researcher, and proofreader. " +
                "Your task is to review the following JSON array of quiz questions.\n" +
                "1. Check for spelling, grammar, punctuation, and clear phrasing in all languages.\n" +
                "2. Check for factual, logical, and conceptual correctness of the questions, answer choices, and explanations.\n" +
                "3. Correct any errors or inaccuracies you find.\n\n" +
                strictFormatRules;
        }

        Console.WriteLine($"Connecting to Gemini API using model: {selectedModel}...");
        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);

        // Configure generation parameters to unlock the maximum output capacity of 65,536 tokens
        var generationConfig = new GenerationConfig
        {
            MaxOutputTokens = 65536
        };

        string fullPrompt = $"{systemPrompt}\n\nHere is the JSON to check:\n{originalJson}";

        // Start request and track elapsed execution time
        Console.WriteLine("Sending data to Gemini for review. Please wait...");
        var stopwatch = Stopwatch.StartNew();
        var verifyTask = model.GenerateContent(fullPrompt, generationConfig: generationConfig);

        Console.Write("Elapsed: 00:00");

        while (!verifyTask.IsCompleted)
        {
            await Task.WhenAny(verifyTask, Task.Delay(1000));
            if (!verifyTask.IsCompleted)
            {
                Console.Write($"\rElapsed: {stopwatch.Elapsed:mm\\:ss}   ");
            }
        }

        stopwatch.Stop();
        Console.WriteLine($"\rCompleted in: {stopwatch.Elapsed:mm\\:ss}!      ");

        var response = await verifyTask;

        if (response == null || string.IsNullOrWhiteSpace(response.Text))
        {
            Console.WriteLine("Error: Gemini returned an empty response or the request was blocked.");
            Environment.Exit(1);
        }

        string processedJson = response.Text.Trim();

        // Check if the response contains a JSON array
        int firstBracket = processedJson.IndexOf('[');
        int lastBracket = processedJson.LastIndexOf(']');

        if (firstBracket == -1 || lastBracket == -1 || lastBracket <= firstBracket)
        {
            // Check if Gemini returned conversational text indicating that everything is already correct
            string lowerText = processedJson.ToLowerInvariant();
            bool isAlreadyCorrect = lowerText.Contains("correct") ||
                                     lowerText.Contains("no changes") ||
                                     lowerText.Contains("looks good") ||
                                     lowerText.Contains("no errors") ||
                                     lowerText.Contains("perfect") ||
                                     lowerText.Contains("already");

            if (isAlreadyCorrect)
            {
                Console.WriteLine("No changes detected.");
                return;
            }

            Console.WriteLine("\n[ERROR] Gemini did not return a valid JSON array.");
            Console.WriteLine("Response received from Gemini:");
            Console.WriteLine(processedJson);
            Environment.Exit(1);
        }

        // Extract the JSON array cleanly
        processedJson = processedJson.Substring(firstBracket, lastBracket - firstBracket + 1);

        // Verify if it is a valid JSON before saving
        try
        {
            using var doc = JsonDocument.Parse(processedJson);
        }
        catch (JsonException ex)
        {
            Console.WriteLine("\n[ERROR] Response from Gemini is not a valid JSON structure.");
            Console.WriteLine($"Details: {ex.Message}");
            if (ex.LineNumber.HasValue)
            {
                Console.WriteLine($"Line: {ex.LineNumber.Value}, Position: {ex.BytePositionInLine}");
            }

            bool isQuestionsDump = response.Text.Contains("question_id") || response.Text.Contains("explanation");
            if (!isQuestionsDump)
            {
                Console.WriteLine("\nRaw response received from Gemini (which is not a JSON array of questions):");
                Console.WriteLine(response.Text.Trim());
            }
            else
            {
                Console.WriteLine("\n(The response contains a corrupted questions array. To avoid spamming, the raw JSON is not printed.)");
            }

            Console.WriteLine("Please fix the prompt rules or verify the source data size.");
            Environment.Exit(1);
        }

        // Check if any changes were made
        if (originalJson.Trim() == processedJson)
        {
            Console.WriteLine("No changes detected.");
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
            Console.WriteLine($"Changes detected! Corrected file saved to: {outputFileName}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred: {ex.Message}");
        Environment.Exit(1);
    }
}

// ==========================================
// 5. MERGE AND ANALYZE LOGIC
// ==========================================
async Task MergeAndAnalyzeAsync(string directoryPath)
{
    try
    {
        Console.WriteLine($"Analyzing directory: {directoryPath}...");

        var regex = new Regex(@"^.+_\d+_\d+(_chn)?\.json$", RegexOptions.IgnoreCase);

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json")
            .Where(f => regex.IsMatch(Path.GetFileName(f)))
            .ToList();

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("No chunk files matching the pattern (e.g. *_[group]_[start].json) found.");
            return;
        }

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

        var orderedQuestions = allQuestions
            .GroupBy(q => new { q.GroupIndex, q.QuestionId, Lang = q.Lang.ToLower().Trim() })
            .Select(g => g.First())
            .OrderBy(q => q.QuestionId)
            .ToList();

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
        PrintStatistics(orderedQuestions);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred during merging: {ex.Message}");
    }
}

// ==========================================
// 6. SPLIT LOGIC
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

        int maxId = questions.Max(q => q.QuestionId);
        int width = maxId.ToString().Length;

        Console.WriteLine($"Maximum Question ID: {maxId}. Filename index padding width: {width}");

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

            var idBlocks = groupQuestions
                .GroupBy(q => (q.QuestionId - 1) / chunkSize)
                .OrderBy(g => g.Key);

            foreach (var idBlock in idBlocks)
            {
                int blockKey = idBlock.Key;
                int startIndex = (blockKey * chunkSize) + 1;
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

// ==========================================
// 7. JSON ANALYSIS LOGIC
// ==========================================
async Task AnalyzeJsonFileAsync(string filePath)
{
    try
    {
        Console.WriteLine($"Reading file for analysis: {filePath}...");
        string jsonContent = await File.ReadAllTextAsync(filePath);

        using var doc = JsonDocument.Parse(jsonContent);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var questions = JsonSerializer.Deserialize<QuestionItem[]>(jsonContent, options);
        if (questions == null || questions.Length == 0)
        {
            Console.WriteLine("The file does not contain any questions or could not be deserialized.");
            return;
        }

        Console.WriteLine($"JSON syntax is valid. Total records: {questions.Length}");
        PrintStatistics(questions);
    }
    catch (JsonException jsonEx)
    {
        Console.WriteLine($"[ERROR] JSON format is invalid: {jsonEx.Message}");
        if (jsonEx.LineNumber.HasValue)
        {
            Console.WriteLine($"Line: {jsonEx.LineNumber.Value}, Position: {jsonEx.BytePositionInLine}");
        }
        Environment.Exit(1);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        Environment.Exit(1);
    }
}

// Helper method to display statistics
void PrintStatistics(IEnumerable<QuestionItem> questions)
{
    Console.WriteLine("\n================ STATISTICS ================");

    int totalUniqueQuestions = questions.Select(q => q.QuestionId).Distinct().Count();
    Console.WriteLine($"Total unique questions: {totalUniqueQuestions}");

    var groups = questions.GroupBy(q => q.GroupIndex).OrderBy(g => g.Key);
    foreach (var group in groups)
    {
        int groupUniqueQuestions = group.Select(q => q.QuestionId).Distinct().Count();
        Console.WriteLine($"Group {group.Key}: {groupUniqueQuestions} total questions");

        var langs = group.Select(q => q.Lang.ToLower().Trim()).Distinct().OrderBy(l => l);
        foreach (var lang in langs)
        {
            int langCount = group.Count(q => q.Lang.Equals(lang, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"Group {group.Key}: {langCount} questions ({lang})");
        }

        var idGroups = group.GroupBy(q => q.QuestionId).OrderBy(g => g.Key);
        foreach (var idGroup in idGroups)
        {
            var langsForId = string.Join(", ", idGroup.Select(q => q.Lang.ToLower().Trim()));
            Console.WriteLine($"  Group {group.Key}, Question ID {idGroup.Key}: {idGroup.Count()} entries ({langsForId})");
        }
    }
    Console.WriteLine("============================================");
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
