using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

if (args.Length < 1)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  View statistics (file):   GeminiChecker <input_file_path> --stats (or --stat, -st, -a, --analyze)");
    Console.WriteLine("  Sort questions by ID:     GeminiChecker <input_file_path> --sort (or -srt)");
    Console.WriteLine("  Split questions:          GeminiChecker <input_file_path> [--split (or -spl)] [--count <int>]");
    Console.WriteLine("  Merge & Analyze folder:   GeminiChecker <input_directory_path>");
    Console.WriteLine("  Verify file with Gemini:  GeminiChecker <input_file_path> --check (or -c) --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("  Generate questions:       GeminiChecker --generate (or -gen) --topics <topics_json_path> --group <index> --count <int> --level <Junior/Middle> --start-id <int> --key <api_key> [--model <model_name>] [--prompt <prompt_file_path>]");
    Console.WriteLine("  Export prompt to file:    GeminiChecker --save-prompt (or -sp, --dump-prompt, -dp) --topics <topics_json_path> --group <index> --count <int> --level <Junior/Middle> --start-id <int> [--prompt <prompt_file_path>]");
    Console.WriteLine("  Generate topic groups:    GeminiChecker --generate-topics (or -gt) --topics <sample_topics_json> --subject <name> [--count <total_count>] [--batch <batch_size>] [--provider <gemini/local>] [--key <api_key>] [--model <model_name>] [--local-url <url>]");
    Console.WriteLine("  Multi-stage pipeline:     GeminiChecker --pipeline (or -pipe) --topics <topics_json_path> --group <index> [--count <int>] [--start-id <int>] [--provider <gemini/local>] [--key <api_key>] [--model <model_name>] [--local-url <url>] [--prompt <prompt_file_path>]");
    Console.WriteLine("\nOptions:");
    Console.WriteLine("  -st, --stat, --stats      Show detailed question statistics (totals, per group, per language).");
    Console.WriteLine("  -a, --analyze             Alias for statistics and JSON validation.");
    Console.WriteLine("  -srt, --sort              Sort questions in the file by question_id (and language) and save to *_sorted.json.");
    Console.WriteLine("  -spl, --split             Explicitly trigger split mode on the specified file.");
    Console.WriteLine("  -c, --check               Trigger Gemini verification/correction mode on the specified file.");
    Console.WriteLine("  -gen, --generate          Trigger Gemini question generation mode.");
    Console.WriteLine("  -sp, --save-prompt        Assemble and save the complete generation prompt to a text file without calling API.");
    Console.WriteLine("  -dp, --dump-prompt        Alias for --save-prompt.");
    Console.WriteLine("  -pipe, --pipeline         Execute multi-stage interview pipeline (Junior -> Middle, EN-first, review, translate 1-by-1).");
    Console.WriteLine("  -gt, --generate-topics    Generate new topic groups based on a sample topics.json.");
    Console.WriteLine("  --subject <string>        Target subject or programming language for topic generation.");
    Console.WriteLine("  -b, --batch <int>         Batch size per iteration for topic generation (default: 5).");
    Console.WriteLine("  --batch-size <int>        Alias for --batch.");
    Console.WriteLine("  --provider <string>       AI provider: 'gemini' or 'local' (default: gemini).");
    Console.WriteLine("  --local-url <url>         Base URL for local OpenAI-compatible endpoint (default: http://localhost:1234/v1).");
    Console.WriteLine("  -t, --topics <path>       Path to topics.json file for subject matter matching.");
    Console.WriteLine("  -g, -grp, --group <idx>   Target group index (topic) for generation (default: 0).");
    Console.WriteLine("  -cnt, --count <int>       Number of unique questions per chunk / level / total topic groups (default: 8).");
    Console.WriteLine("  -l, --level <string>      Difficulty level (e.g., Junior, Middle, senior) (default: Junior).");
    Console.WriteLine("  -s, --start-id <int>      Starting question_id for newly generated questions (default: 1).");
    Console.WriteLine("  -k, --key <api_key>       Your Google AI Studio API Key (fallback: GEMINI_API_KEY environment variable).");
    Console.WriteLine("  -m, --model <model_name>  Select AI model name (default: gemini-3.6-flash).");
    Console.WriteLine("  -p, --prompt <file_path>  Path to an external text file containing custom system prompt rules.");
    Console.WriteLine("\nExamples:");
    Console.WriteLine("  1. View question statistics:");
    Console.WriteLine("     GeminiChecker questions.json --stats");
    Console.WriteLine("  2. Split question file into chunks of 8:");
    Console.WriteLine("     GeminiChecker questions.json --split --count 8");
    Console.WriteLine("  3. Merge chunks and analyze directory:");
    Console.WriteLine("     GeminiChecker ./chunks");
    Console.WriteLine("  4. Verify questions file with Gemini:");
    Console.WriteLine("     GeminiChecker questions_0_0001.json --check --key <api_key>");
    Console.WriteLine("  5. Generate 15 topics in batches of 5 (Local AI):");
    Console.WriteLine("     GeminiChecker --generate-topics --topics topics_sample.json --subject \"C# Advanced\" --count 15 --batch 5 --provider local --model \"gemma-4-e4b\"");
    Console.WriteLine("  6. Generate 20 topics in batches of 5 (Gemini):");
    Console.WriteLine("     GeminiChecker --generate-topics --topics topics_sample.json --subject \"Go Concurrency\" --count 20 --batch 5 --provider gemini --key <api_key>");
    Console.WriteLine("  7. Run multi-stage pipeline (Junior -> Middle) via Local AI (LM Studio / Ollama):");
    Console.WriteLine("     GeminiChecker --pipeline --topics topics.json --group 0 --count 8 --start-id 1 --provider local --model \"gemma-4-e4b\"");
    Console.WriteLine("  8. Run multi-stage pipeline via Cloud Gemini:");
    Console.WriteLine("     GeminiChecker --pipeline --topics topics.json --group 0 --count 8 --start-id 1 --provider gemini --key <api_key>");
    Console.WriteLine("  9. Export generation prompt to file without calling API:");
    Console.WriteLine("     GeminiChecker --save-prompt --topics topics.json --group 0 --count 8 --level Junior --start-id 1");
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

bool isPipelineMode = args.Contains("--pipeline", StringComparer.OrdinalIgnoreCase) ||
                      args.Contains("-pipe", StringComparer.OrdinalIgnoreCase);

bool isGenerateTopicsMode = args.Contains("--generate-topics", StringComparer.OrdinalIgnoreCase) ||
                            args.Contains("-gt", StringComparer.OrdinalIgnoreCase);

bool isCheckMode = args.Contains("--check", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-c", StringComparer.OrdinalIgnoreCase);

bool isSplitMode = args.Contains("--split", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("-spl", StringComparer.OrdinalIgnoreCase);

bool isSortMode = args.Contains("--sort", StringComparer.OrdinalIgnoreCase) ||
                  args.Contains("-srt", StringComparer.OrdinalIgnoreCase);

bool isAnalyzeMode = args.Contains("--analyze", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("-a", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("--stats", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("--stat", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("-st", StringComparer.OrdinalIgnoreCase);

string topicsPath = string.Empty;
int groupIndex = 0;
int count = 8; // Reused as chunkSize, questionCount, or total topic groups count
int batchSize = 5; // Batch size per iteration for topic generation
string level = "Junior";
int startId = 1;
string promptFilePath = string.Empty;
string apiKey = string.Empty;
string modelName = "gemini-3.6-flash";
string inputPath = null;
string provider = "gemini";
string subject = string.Empty;
string localUrl = "http://localhost:1234/v1";

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

// Search for batch size flag (count per iteration)
int batchIndex = Array.FindIndex(args, arg => arg.Equals("--batch", StringComparison.OrdinalIgnoreCase) ||
                                              arg.Equals("--batch-size", StringComparison.OrdinalIgnoreCase) ||
                                              arg.Equals("-b", StringComparison.OrdinalIgnoreCase));
if (batchIndex != -1 && batchIndex + 1 < args.Length)
{
    if (int.TryParse(args[batchIndex + 1], out int bs) && bs > 0) batchSize = bs;
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

// Search for provider flag
int providerIndex = Array.FindIndex(args, arg => arg.Equals("--provider", StringComparison.OrdinalIgnoreCase));
if (providerIndex != -1 && providerIndex + 1 < args.Length)
{
    provider = args[providerIndex + 1].ToLowerInvariant();
}

// Search for subject flag
int subjectIndex = Array.FindIndex(args, arg => arg.Equals("--subject", StringComparison.OrdinalIgnoreCase));
if (subjectIndex != -1 && subjectIndex + 1 < args.Length)
{
    subject = args[subjectIndex + 1];
}

// Search for local API URL flag
int localUrlIndex = Array.FindIndex(args, arg => arg.Equals("--local-url", StringComparison.OrdinalIgnoreCase));
if (localUrlIndex != -1 && localUrlIndex + 1 < args.Length)
{
    localUrl = args[localUrlIndex + 1];
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
    if (arg.Equals("--pipeline", StringComparison.OrdinalIgnoreCase) || arg.Equals("-pipe", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    if (arg.Equals("--generate-topics", StringComparison.OrdinalIgnoreCase) || arg.Equals("-gt", StringComparison.OrdinalIgnoreCase))
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
    if (arg.Equals("--sort", StringComparison.OrdinalIgnoreCase) || arg.Equals("-srt", StringComparison.OrdinalIgnoreCase))
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
    if (arg.Equals("--batch", StringComparison.OrdinalIgnoreCase) || arg.Equals("--batch-size", StringComparison.OrdinalIgnoreCase) || arg.Equals("-b", StringComparison.OrdinalIgnoreCase))
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
    if (arg.Equals("--provider", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--subject", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }
    if (arg.Equals("--local-url", StringComparison.OrdinalIgnoreCase))
    {
        i++; // Skip its value
        continue;
    }

    inputPath = arg;
    break;
}

if (string.IsNullOrEmpty(inputPath) && !isGenerateMode && !isSavePromptMode && !isPipelineMode && !isGenerateTopicsMode)
{
    Console.WriteLine("Error: Missing input file or directory path.");
    Environment.Exit(1);
}

// Router to appropriate functionality based on inputs
if (isSavePromptMode)
{
    await SavePromptToFileAsync(topicsPath, groupIndex, count, level, startId, promptFilePath);
}
else if (isGenerateTopicsMode)
{
    await GenerateTopicsAsync(topicsPath, subject, count, batchSize, provider, apiKey, modelName, localUrl);
}
else if (isPipelineMode)
{
    await RunInterviewPipelineAsync(topicsPath, groupIndex, count, level, startId, provider, apiKey, modelName, localUrl, promptFilePath);
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
else if (isSortMode)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: File '{inputPath}' not found for sorting.");
        Environment.Exit(1);
    }
    await SortQuestionsByIdAsync(inputPath);
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
// 0. JSON CLEANER HELPER
// ==========================================
string CleanJsonString(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return raw;

    // Strip internal thought blocks from reasoning models (e.g. <thought>...</thought>)
    string cleaned = Regex.Replace(raw, @"<thought>.*?</thought>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

    // Extract cleanly between JSON array brackets [ ... ]
    int firstBracket = cleaned.IndexOf('[');
    int lastBracket = cleaned.LastIndexOf(']');

    if (firstBracket != -1 && lastBracket != -1 && lastBracket > firstBracket)
    {
        return cleaned.Substring(firstBracket, lastBracket - firstBracket + 1);
    }

    // Fallback: Check for JSON object { ... }
    int firstBrace = cleaned.IndexOf('{');
    int lastBrace = cleaned.LastIndexOf('}');
    if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
    {
        return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
    }

    // Fallback: Strip markdown code fences if brackets were not found cleanly
    if (cleaned.StartsWith("```"))
    {
        int firstLineEnd = cleaned.IndexOf('\n');
        if (firstLineEnd != -1)
        {
            cleaned = cleaned.Substring(firstLineEnd).Trim();
        }
        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }
    }

    return cleaned;
}

// ==========================================
// UNIFIED AI CLIENT CALLER WITH 1-SEC TIMER
// ==========================================
async Task<string> CallAiAsync(string prompt, string providerType, string apiToken, string selectedModel, string localApiUrl)
{
    var stopwatch = Stopwatch.StartNew();
    Console.Write("    [AI Request] Elapsed: 00:00");

    if (providerType.Equals("local", StringComparison.OrdinalIgnoreCase))
    {
        // Call local OpenAI-compatible endpoint (LM Studio, Ollama, etc.)
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var requestPayload = new
        {
            model = selectedModel,
            messages = new[]
            {
                new { role = "system", content = "You are a precise technical generator. You must output ONLY valid JSON without markdown fences, without thought blocks, and without conversational text." },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            max_tokens = 8192
        };

        string endpoint = $"{localApiUrl.TrimEnd('/')}/chat/completions";
        var stringContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

        try
        {
            var responseTask = httpClient.PostAsync(endpoint, stringContent);

            while (!responseTask.IsCompleted)
            {
                await Task.WhenAny(responseTask, Task.Delay(1000));
                if (!responseTask.IsCompleted)
                {
                    Console.Write($"\r    [AI Request] Elapsed: {stopwatch.Elapsed:mm\\:ss}   ");
                }
            }

            stopwatch.Stop();
            var response = await responseTask;
            response.EnsureSuccessStatusCode();

            Console.WriteLine($"\r    [AI Request] Completed in: {stopwatch.Elapsed:mm\\:ss}!      ");

            string responseBody = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseBody);
            var root = jsonDoc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            Console.WriteLine($"\r    [ERROR] AI request timed out after {stopwatch.Elapsed:mm\\:ss}.      ");
            return string.Empty;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"\r    [ERROR] AI request failed: {ex.Message}      ");
            return string.Empty;
        }
    }
    else
    {
        // Call cloud Gemini API
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            Console.WriteLine("Error: Gemini API Key is missing. Please provide it using --key / -k option or set GEMINI_API_KEY environment variable.");
            Environment.Exit(1);
        }

        var googleAI = new GoogleAI(apiKey: apiToken);
        var model = googleAI.GenerativeModel(model: selectedModel);
        var generationConfig = new GenerationConfig
        {
            MaxOutputTokens = 65536
        };

        try
        {
            var responseTask = model.GenerateContent(prompt, generationConfig: generationConfig);

            while (!responseTask.IsCompleted)
            {
                await Task.WhenAny(responseTask, Task.Delay(1000));
                if (!responseTask.IsCompleted)
                {
                    Console.Write($"\r    [AI Request] Elapsed: {stopwatch.Elapsed:mm\\:ss}   ");
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"\r    [AI Request] Completed in: {stopwatch.Elapsed:mm\\:ss}!      ");

            var response = await responseTask;
            return response?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"\r    [ERROR] Gemini request failed: {ex.Message}      ");
            return string.Empty;
        }
    }
}

// ==========================================
// TOPIC GENERATOR LOGIC (GENERIC, CHECKPOINTED & SEARCH-GROUNDED)
// ==========================================
async Task GenerateTopicsAsync(
    string sampleTopicsPath,
    string targetSubject,
    int totalCount,
    int batchSize,
    string providerType,
    string apiToken,
    string selectedModel,
    string localApiUrl)
{
    if (string.IsNullOrWhiteSpace(sampleTopicsPath) || !File.Exists(sampleTopicsPath))
    {
        Console.WriteLine($"Error: Reference topics file '{sampleTopicsPath}' not found.");
        Environment.Exit(1);
    }

    if (string.IsNullOrWhiteSpace(targetSubject))
    {
        Console.WriteLine("Error: Please provide a target subject using --subject (e.g., --subject <technology_name>).");
        Environment.Exit(1);
    }

    if (totalCount <= 0) totalCount = 8;
    if (batchSize <= 0) batchSize = 4;

    string directory = Path.GetDirectoryName(sampleTopicsPath);
    if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();

    string safeSubjectName = Regex.Replace(targetSubject.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "_");
    string draftEnPath = Path.Combine(directory, $"draft_topics_en_{safeSubjectName}.json");
    string draftTransPath = Path.Combine(directory, $"draft_topics_{safeSubjectName}.json");
    string finalOutPath = Path.Combine(directory, $"topics_{safeSubjectName}.json");

    Console.WriteLine($"\n[TOPIC GENERATION] Target: {totalCount} broad interview categories for '{targetSubject}'...");

    // ---------------------------------------------------------
    // LIVE WEB SEARCH GROUNDING FOR TOPICS (10 SOURCES)
    // ---------------------------------------------------------
    string searchQuery = $"{targetSubject} developer interview topics roadmap curriculum";
    Console.WriteLine($"\n[SEARCH] Performing live web search for topic roadmaps: \"{searchQuery}\"...");
    string searchResults = await SearchWebAsync(searchQuery, 10);

    string searchContext = !string.IsNullOrWhiteSpace(searchResults)
        ? $"\nAUTHENTIC US/EU TOPIC ROADMAP & CURRICULUM SNIPPETS:\n- {searchResults}\n\n" +
          $"Use the actual topic categories, syllabus, and structure found in the web research above to organize the categories.\n"
        : string.Empty;

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    var writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // -------------------------------------------------------------
    // STEP 1: GENERATE ENGLISH TOPICS (WITH RESUME CHECKPOINT)
    // -------------------------------------------------------------
    var englishTopics = new List<string>();

    if (File.Exists(draftEnPath))
    {
        try
        {
            string existingEn = CleanJsonString(await File.ReadAllTextAsync(draftEnPath));
            var loadedEn = JsonSerializer.Deserialize<List<string>>(existingEn, options);
            if (loadedEn != null && loadedEn.Count > 0)
            {
                englishTopics = loadedEn;
                Console.WriteLine($"[RESUME] Found existing English draft: {englishTopics.Count} categories loaded from '{Path.GetFileName(draftEnPath)}'.");
            }
        }
        catch { /* Fallback to generating if draft is corrupt */ }
    }

    int iteration = 1;
    while (englishTopics.Count < totalCount)
    {
        int needed = Math.Min(batchSize, totalCount - englishTopics.Count);
        int currentStartIndex = englishTopics.Count;
        int currentEndIndex = currentStartIndex + needed - 1;

        Console.WriteLine($"\n[Iteration {iteration}] Generating category slots {currentStartIndex} to {currentEndIndex} ({englishTopics.Count}/{totalCount} total)...");

        string structuralRule = string.Empty;
        if (currentStartIndex == 0)
        {
            structuralRule += "- SLOT 0 (CRITICAL FIRST TOPIC): MUST ALWAYS be the core foundational topic: 'Core " + targetSubject + " & Language Fundamentals'.\n";
        }
        if (currentEndIndex == totalCount - 1)
        {
            structuralRule += "- FINAL SLOT (CRITICAL LAST TOPIC): MUST ALWAYS be practical problem-solving: 'Practical Tasks & Problem Solving'.\n";
        }

        string previousTopicsPrompt = englishTopics.Count > 0
            ? "ALREADY ASSIGNED SLOTS (DO NOT DUPLICATE):\n" + string.Join("\n", englishTopics.Select((t, i) => $"Slot {i}: {t}")) + "\n"
            : string.Empty;

        string enPrompt =
            $"You are an expert technical curriculum architect designing interview categories.\n" +
            $"TASK: Generate exactly {needed} BROAD, HIGH-LEVEL interview category titles specifically for '{targetSubject}'.\n" +
            $"Slots to fill: from Slot {currentStartIndex} to Slot {currentEndIndex} out of {totalCount} total categories.\n\n" +
            $"{searchContext}" +
            $"STRICT STRUCTURAL RULES:\n" +
            $"{structuralRule}" +
            $"- BROAD PILLARS ONLY: Topics must be broad, overarching curriculum categories appropriate for {targetSubject} (e.g. core syntax, data structures, concurrency, persistence/storage, ecosystem/frameworks, architecture/design), NOT narrow micro-topics or individual library methods.\n" +
            $"- LOGICAL PROGRESSION: Follow a natural curriculum progression from fundamentals up to architecture and practical coding.\n" +
            $"- NO INTERNAL MONOLOGUE: Do NOT output <thought> tags, explanations, or thinking blocks.\n\n" +
            $"{previousTopicsPrompt}\n" +
            $"CRITICAL OUTPUT FORMAT:\n" +
            $"Return ONLY a JSON array of strings containing exactly {needed} category titles. Begin your response directly with '[' and end with ']'.\n" +
            $"Example format: [\"Category 1\", \"Category 2\"]";

        string rawEn = await CallAiAsync(enPrompt, providerType, apiToken, selectedModel, localApiUrl);
        string cleanedEn = CleanJsonString(rawEn);

        try
        {
            var batchTopics = JsonSerializer.Deserialize<List<string>>(cleanedEn, options);
            if (batchTopics != null && batchTopics.Count > 0)
            {
                foreach (var topic in batchTopics)
                {
                    string trimmed = topic.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) &&
                        !englishTopics.Any(et => string.Equals(et, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        englishTopics.Add(trimmed);
                        Console.WriteLine($"  [Slot {englishTopics.Count - 1}] -> \"{trimmed}\"");
                        if (englishTopics.Count >= totalCount) break;
                    }
                }

                // Save checkpoint after every batch
                await File.WriteAllTextAsync(draftEnPath, JsonSerializer.Serialize(englishTopics, writeOptions));
                Console.WriteLine($"  [CHECKPOINT] Updated English draft: {draftEnPath}");
            }
            else
            {
                Console.WriteLine("[WARN] Received empty batch response. Retrying iteration...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] JSON parsing failed: {ex.Message}. Retrying iteration...");
        }

        iteration++;
        if (iteration > (totalCount / batchSize + 5))
        {
            Console.WriteLine("[WARN] Maximum iteration limit reached. Proceeding with collected topics.");
            break;
        }
    }

    Console.WriteLine($"\n[SUCCESS] Category structure finalized: {englishTopics.Count} categories ready.");

    // -------------------------------------------------------------
    // STEP 2: TRANSLATE ONE-BY-ONE WITH INSTANT CHECKPOINTING
    // -------------------------------------------------------------
    Console.WriteLine("\n[TRANSLATION] Translating categories one by one into 5 languages (en, uk, de, es, fr)...");
    var allTopicItems = new List<TopicItem>();

    if (File.Exists(draftTransPath))
    {
        try
        {
            string existingTrans = CleanJsonString(await File.ReadAllTextAsync(draftTransPath));
            var loadedTrans = JsonSerializer.Deserialize<List<TopicItem>>(existingTrans, options);
            if (loadedTrans != null && loadedTrans.Count > 0)
            {
                allTopicItems = loadedTrans;
                Console.WriteLine($"[RESUME] Found existing translated draft: {allTopicItems.Count} records loaded from '{Path.GetFileName(draftTransPath)}'.");
            }
        }
        catch { /* Fallback to empty list if draft is corrupt */ }
    }

    // Determine which groups are already completely translated
    var completedGroups = allTopicItems
        .GroupBy(t => t.GroupIndex)
        .Where(g => g.Select(x => x.Lang.ToLower().Trim()).Distinct().Count() == 5)
        .Select(g => g.Key)
        .ToHashSet();

    for (int i = 0; i < englishTopics.Count; i++)
    {
        string topicName = englishTopics[i];

        if (completedGroups.Contains(i))
        {
            Console.WriteLine($"  [Group {i}] \"{topicName}\" is already translated in draft. Skipping.");
            continue;
        }

        Console.WriteLine($"\n -> Translating Group {i}: \"{topicName}\" into [en, uk, de, es, fr]...");

        string transPrompt =
            $"Translate the following single technical interview category into exactly 5 languages: 'en', 'uk', 'de', 'es', 'fr'.\n" +
            $"Category: \"{topicName}\"\n" +
            $"Target Group Index: {i}\n\n" +
            $"RULES:\n" +
            $"- Provide accurate, natural technical terminology used by software engineers in each language.\n" +
            $"- group_index MUST be strictly {i}.\n" +
            $"- lang MUST be one of: 'en', 'uk', 'de', 'es', 'fr'.\n" +
            $"- NO INTERNAL MONOLOGUE: Output raw JSON immediately without thought blocks.\n\n" +
            $"OUTPUT FORMAT: Return ONLY a valid JSON array of 5 objects with keys: \"group_index\", \"lang\", \"name\". Begin directly with '[' and end with ']'.";

        bool success = false;
        for (int attempt = 1; attempt <= 2 && !success; attempt++)
        {
            try
            {
                string rawTrans = await CallAiAsync(transPrompt, providerType, apiToken, selectedModel, localApiUrl);
                string cleanedTrans = CleanJsonString(rawTrans);

                var translatedItems = JsonSerializer.Deserialize<List<TopicItem>>(cleanedTrans, options);
                if (translatedItems != null && translatedItems.Count == 5)
                {
                    // Remove any stale entries for this group and add new ones
                    allTopicItems.RemoveAll(t => t.GroupIndex == i);
                    allTopicItems.AddRange(translatedItems);
                    Console.WriteLine("    Successfully translated into 5 languages!");
                    success = true;

                    // Save checkpoint immediately after this topic
                    await File.WriteAllTextAsync(draftTransPath, JsonSerializer.Serialize(allTopicItems, writeOptions));
                    Console.WriteLine($"    [CHECKPOINT] Saved progress to '{Path.GetFileName(draftTransPath)}'.");
                }
                else
                {
                    Console.WriteLine($"[WARN] Attempt {attempt}: Received incomplete translation array.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Attempt {attempt} failed: {ex.Message}");
            }
        }

        if (!success)
        {
            // Fallback: register English item so the slot is preserved
            allTopicItems.RemoveAll(t => t.GroupIndex == i);
            allTopicItems.Add(new TopicItem { GroupIndex = i, Lang = "en", Name = topicName });
            await File.WriteAllTextAsync(draftTransPath, JsonSerializer.Serialize(allTopicItems, writeOptions));
            Console.WriteLine($"    [FALLBACK] Preserved English category for group {i}.");
        }
    }

    // -------------------------------------------------------------
    // STEP 3: FORMAT AND SAVE FINAL TOPICS FILE
    // -------------------------------------------------------------
    var langOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "en", 1 },
        { "uk", 2 },
        { "de", 3 },
        { "es", 4 },
        { "fr", 5 }
    };

    var orderedTopics = allTopicItems
        .OrderBy(t => t.GroupIndex)
        .ThenBy(t => langOrder.TryGetValue(t.Lang?.Trim() ?? string.Empty, out int ord) ? ord : 99)
        .ToList();

    await File.WriteAllTextAsync(finalOutPath, JsonSerializer.Serialize(orderedTopics, writeOptions));
    Console.WriteLine($"\n[ALL DONE] Successfully generated and translated {englishTopics.Count} topic groups ({orderedTopics.Count} total records).");
    Console.WriteLine($"Saved to: {finalOutPath}");
}

// ==========================================
// WEB SEARCH HELPER (RESILIENT SSL & MULTI-ENDPOINT SCRAPER)
// ==========================================
async Task<string> SearchWebAsync(string query, int maxResults = 10)
{
    try
    {
        var handler = new HttpClientHandler
        {
            // Bypass antivirus / local proxy SSL inspection issues (e.g. ESET, Kaspersky, VPN)
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.Timeout = TimeSpan.FromSeconds(15);

        // Try DuckDuckGo Lite endpoint first (more permissive with TLS handshakes), fallback to HTML
        string encodedQuery = Uri.EscapeDataString(query);
        string[] searchEndpoints = new[]
        {
            $"https://lite.duckduckgo.com/lite/?q={encodedQuery}",
            $"https://html.duckduckgo.com/html/?q={encodedQuery}"
        };

        var snippets = new List<string>();

        foreach (var searchUrl in searchEndpoints)
        {
            try
            {
                string html = await client.GetStringAsync(searchUrl);

                // Pattern for DuckDuckGo Lite (.result-snippet) and HTML (.result__snippet)
                var snippetRegex = new Regex(@"<(td|a)[^>]*class=""(result-snippet|result__snippet)[^""]*""[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var matches = snippetRegex.Matches(html);

                foreach (Match m in matches)
                {
                    if (m.Success && m.Groups.Count > 3)
                    {
                        string rawText = m.Groups[3].Value;
                        string cleanText = Regex.Replace(rawText, "<.*?>", string.Empty);
                        cleanText = System.Net.WebUtility.HtmlDecode(cleanText).Trim();
                        if (!string.IsNullOrWhiteSpace(cleanText) && cleanText.Length > 20)
                        {
                            snippets.Add(cleanText);
                            if (snippets.Count >= maxResults) break;
                        }
                    }
                }

                if (snippets.Count > 0) break;
            }
            catch
            {
                // Fall through to next endpoint if current one fails
            }
        }

        // Print retrieved web research directly to the console
        if (snippets.Count > 0)
        {
            Console.WriteLine($"\n  ------------------ LIVE WEB SEARCH RESULTS ({snippets.Count} items) ------------------");
            for (int i = 0; i < snippets.Count; i++)
            {
                Console.WriteLine($"  [{i + 1}] {snippets[i]}");
            }
            Console.WriteLine("  ------------------------------------------------------------------------\n");
        }
        else
        {
            Console.WriteLine("\n  [INFO] No web snippets returned for this query.\n");
        }

        return snippets.Count > 0 ? string.Join("\n- ", snippets) : string.Empty;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n    [WARN] Web search query failed: {ex.Message}");
        return string.Empty;
    }
}

// ==========================================
// MULTI-STAGE INTERVIEW PIPELINE LOGIC (SINGLE LEVEL)
// ==========================================
async Task RunInterviewPipelineAsync(
    string tPath,
    int gIdx,
    int questionCount,
    string targetLevel,
    int startingId,
    string providerType,
    string apiToken,
    string selectedModel,
    string localApiUrl,
    string promptRulesPath)
{
    if (string.IsNullOrWhiteSpace(tPath) || !File.Exists(tPath))
    {
        Console.WriteLine($"Error: Topics specification file '{tPath}' not found.");
        Environment.Exit(1);
    }

    string directory = Path.GetDirectoryName(tPath);
    if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();

    Console.WriteLine($"\nReading topics file: {tPath}...");
    string topicsContent = CleanJsonString(await File.ReadAllTextAsync(tPath));

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    var allTopics = JsonSerializer.Deserialize<TopicItem[]>(topicsContent, options);
    var groupTopics = allTopics?.Where(t => t.GroupIndex == gIdx).ToList();

    if (groupTopics == null || groupTopics.Count == 0)
    {
        Console.WriteLine($"Error: No topics found for group index {gIdx} in '{tPath}'.");
        Environment.Exit(1);
    }

    string topicNameEn = groupTopics.FirstOrDefault(t => t.Lang.Equals("en", StringComparison.OrdinalIgnoreCase))?.Name ?? "General Technical";
    string topicGuidelines = string.Join("\n", groupTopics.Select(t => $"- Language '{t.Lang}': Topic name is \"{t.Name}\""));

    // Normalize target level casing
    string normalizedLevel = string.IsNullOrWhiteSpace(targetLevel) ? "Junior" : targetLevel.Trim();
    if (normalizedLevel.Equals("junior", StringComparison.OrdinalIgnoreCase)) normalizedLevel = "Junior";
    else if (normalizedLevel.Equals("middle", StringComparison.OrdinalIgnoreCase)) normalizedLevel = "Middle";
    else if (normalizedLevel.Equals("senior", StringComparison.OrdinalIgnoreCase)) normalizedLevel = "Senior";

    // Compile duplicate blacklist from existing questions in directory
    Console.WriteLine("Scanning directory for pre-existing questions to build exclusion blacklist...");
    var blacklist = new List<string>();
    var regex = new Regex(@"^.+_\d+_\d+.*\.json$", RegexOptions.IgnoreCase);

    foreach (var file in Directory.GetFiles(directory, "*.json").Where(f => regex.IsMatch(Path.GetFileName(f))))
    {
        try
        {
            string content = CleanJsonString(await File.ReadAllTextAsync(file));
            var items = JsonSerializer.Deserialize<QuestionItem[]>(content, options);
            if (items != null)
            {
                var enItems = items.Where(q => q.GroupIndex == gIdx && string.Equals(q.Lang, "en", StringComparison.OrdinalIgnoreCase));
                foreach (var q in enItems)
                {
                    if (!string.IsNullOrWhiteSpace(q.Question)) blacklist.Add(q.Question.Trim());
                }
            }
        }
        catch { /* Ignore corrupted or unrelated json files */ }
    }

    Console.WriteLine($"Compiled {blacklist.Count} unique English questions for deduplication.");

    // Custom system prompt if supplied
    string customPromptRules = string.Empty;
    if (!string.IsNullOrWhiteSpace(promptRulesPath) && File.Exists(promptRulesPath))
    {
        Console.WriteLine($"Loading custom guidelines from: {promptRulesPath}...");
        customPromptRules = await File.ReadAllTextAsync(promptRulesPath) + "\n\n";
    }

    string strictRules =
        "CORE SOURCING AND STRUCTURAL RULES:\n" +
        "- ABSOLUTE PROHIBITION ON BACKTICKS: NEVER use the backtick symbol (`) anywhere in the text. For code elements, function names, types, and keywords, ALWAYS use single quotes ('...') or double quotes (\"...\").\n" +
        "- STANDALONE EDUCATIONAL EXPLANATION REQUIREMENT (STRICT): The 'explanation' must be approximately 5 sentences long, providing a self-contained, authoritative technical explanation of the underlying concept, language rules, runtime behavior, and mechanics that make this outcome true.\n" +
        "- ABSOLUTE BAN ON OPTION AND META REFERENCES: The 'explanation' MUST NEVER mention option letters, labels, or relative choice positioning (STRICTLY FORBIDDEN: 'Option A', 'Choice B', 'answer_win', 'the correct choice', 'the right answer', 'the first option', 'the distractors'). Explain the technical mechanics neutrally and independently as a standalone reference, without commenting on the quiz options.\n" +
        "- All answer choices (a, b, c, d) must be of comparable length and complexity.\n" +
        "- Do NOT include any markdown code fence blocks (like ```json). Return ONLY the raw JSON array.";

    var writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    int endId = startingId + questionCount - 1;
    Console.WriteLine("\n==================================================================");
    Console.WriteLine($"  TARGET LEVEL: {normalizedLevel.ToUpperInvariant()} (ID Range: {startingId} to {endId})");
    Console.WriteLine($"  Topic: Group {gIdx} - \"{topicNameEn}\"");
    Console.WriteLine("==================================================================");

    // ---------------------------------------------------------
    // LIVE WEB SEARCH GROUNDING (WITH PERSISTENT FILE CACHE)
    // ---------------------------------------------------------
    string searchCacheFileName = $"search_context_group_{gIdx}_{normalizedLevel.ToLowerInvariant()}.txt";
    string searchCachePath = Path.Combine(directory, searchCacheFileName);
    string searchResults = string.Empty;

    if (File.Exists(searchCachePath))
    {
        searchResults = await File.ReadAllTextAsync(searchCachePath);
        Console.WriteLine($"\n[SEARCH CACHE] Loaded existing web research from '{searchCacheFileName}'.");
        Console.WriteLine($"  ------------------ CACHED WEB RESEARCH SNIPPETS ------------------");
        var cachedLines = searchResults.Split(new[] { "\n- " }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < cachedLines.Length; i++)
        {
            Console.WriteLine($"  [{i + 1}] {cachedLines[i].TrimStart('-', ' ')}");
        }
        Console.WriteLine("  ------------------------------------------------------------------\n");
    }
    else
    {
        string cleanTopic = Regex.Replace(topicNameEn, @"[&/\\_]+", " ").Trim();
        cleanTopic = Regex.Replace(cleanTopic, @"\s+", " ");
        string searchQuery = $"{cleanTopic} {normalizedLevel} interview questions";

        Console.WriteLine($"\n[SEARCH] Performing live web search: \"{searchQuery}\"...");
        searchResults = await SearchWebAsync(searchQuery, 10);

        // If query returned no snippets, attempt concise fallback query
        if (string.IsNullOrWhiteSpace(searchResults))
        {
            string[] words = cleanTopic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string shortTopic = words.Length >= 2 ? $"{words[0]} {words[1]}" : cleanTopic;
            string fallbackQuery = $"{shortTopic} {normalizedLevel} technical interview questions";
            Console.WriteLine($"  [RETRY SEARCH] Query returned 0 results. Trying concise query: \"{fallbackQuery}\"...");
            searchResults = await SearchWebAsync(fallbackQuery, 10);
        }

        // Cache search results to disk for future runs or manual edits
        if (!string.IsNullOrWhiteSpace(searchResults))
        {
            await File.WriteAllTextAsync(searchCachePath, searchResults);
            Console.WriteLine($"  [SEARCH CACHE] Saved web research to '{searchCacheFileName}'.");
        }
    }

    string searchContext = string.Empty;
    if (!string.IsNullOrWhiteSpace(searchResults))
    {
        searchContext =
            $"\nAUTHENTIC US/EU WEB SEARCH & DOCUMENTATION SNIPPETS:\n" +
            $"- {searchResults}\n\n" +
            $"CRITICAL INSTRUCTION: Base your questions, code scenarios, and distractor choices on the real-world concepts found in the web research snippets above.\n";
    }
    else
    {
        Console.WriteLine("  [INFO] No web research available. Proceeding with internal model knowledge.");
    }

    // ---------------------------------------------------------
    // STEP 1: GENERATE IN ENGLISH ONLY (1 QUESTION AT A TIME)
    // ---------------------------------------------------------
    Console.WriteLine($"\n[1/4] Generating {questionCount} questions in ENGLISH ONLY for level '{normalizedLevel}' (one by one)...");

    string draftFileName = $"draft_{normalizedLevel.ToLowerInvariant()}_en.json";
    string draftPath = Path.Combine(directory, draftFileName);
    var enQuestions = new List<QuestionItem>();

    if (File.Exists(draftPath))
    {
        try
        {
            string existingDraft = CleanJsonString(await File.ReadAllTextAsync(draftPath));
            var loadedItems = JsonSerializer.Deserialize<List<QuestionItem>>(existingDraft, options);
            if (loadedItems != null && loadedItems.Count > 0)
            {
                enQuestions = loadedItems;
                Console.WriteLine($"  [RESUME] Loaded {enQuestions.Count} questions from existing draft '{draftFileName}'.");
            }
        }
        catch { /* Ignore draft if corrupt */ }
    }

    while (enQuestions.Count < questionCount)
    {
        int currentId = startingId + enQuestions.Count;
        char expectedKey = (char)('a' + ((currentId - 1) % 4));

        Console.WriteLine($"\n  -> Generating Question ID {currentId} (Target answer_win: '{expectedKey}') [{enQuestions.Count + 1}/{questionCount}]...");

        string blacklistPrompt = blacklist.Count > 0
            ? "\nCRITICAL: DO NOT DUPLICATE OR REPHRASE ANY OF THESE PRE-EXISTING QUESTIONS:\n" +
              string.Join("\n", blacklist.TakeLast(60).Select((q, idx) => $"{idx + 1}. {q}"))
            : string.Empty;

        string genEnPrompt =
            $"{customPromptRules}" +
            $"You are an expert technical interviewer creating questions for a mobile interview prep app.\n" +
            $"TASK SPECIFICATION:\n" +
            $"- Target Topic: '{topicNameEn}' (Group Index: {gIdx})\n" +
            $"- Difficulty Level: '{normalizedLevel}'\n" +
            $"- Question ID: {currentId}\n" +
            $"- Required answer_win: '{expectedKey}' (Place the correct answer choice strictly at 'answer_{expectedKey}')\n" +
            $"- Language: English only ('lang': 'en')\n\n" +
            $"{searchContext}" +
            $"{strictRules}\n" +
            $"- NO INTERNAL MONOLOGUE: Do NOT output <thought> tags or reasoning text.\n" +
            $"- EXPLANATION RULE: The explanation MUST be completely independent of answer labels (NEVER say 'Option {expectedKey} is correct', 'Choice {expectedKey}', or 'The correct answer is...'). Write an educational technical summary of the concept, mechanics, and rules in principle.\n" +
            $"{blacklistPrompt}\n\n" +
            $"Return ONLY a single valid JSON array containing exactly 1 object with keys: " +
            $"\"question_id\", \"lang\", \"level\", \"group_index\", \"question\", \"answer_a\", \"answer_b\", \"answer_c\", \"answer_d\", \"answer_win\", \"explanation\". Begin directly with '[' and end with ']'.";

        bool qSuccess = false;
        for (int attempt = 1; attempt <= 2 && !qSuccess; attempt++)
        {
            try
            {
                string rawEn = await CallAiAsync(genEnPrompt, providerType, apiToken, selectedModel, localApiUrl);
                string cleanedEn = CleanJsonString(rawEn);

                var items = JsonSerializer.Deserialize<List<QuestionItem>>(cleanedEn, options);
                if (items != null && items.Count > 0)
                {
                    var qItem = items[0];
                    qItem.QuestionId = currentId;
                    qItem.GroupIndex = gIdx;
                    qItem.Level = normalizedLevel;
                    qItem.Lang = "en";
                    qItem.AnswerWin = expectedKey.ToString();

                    enQuestions.Add(qItem);
                    blacklist.Add(qItem.Question.Trim());
                    await File.WriteAllTextAsync(draftPath, JsonSerializer.Serialize(enQuestions, writeOptions));
                    Console.WriteLine($"     Question ID {currentId} generated successfully! Saved to draft ({enQuestions.Count}/{questionCount}).");
                    qSuccess = true;
                }
                else
                {
                    Console.WriteLine($"    [WARN] Attempt {attempt}: Received empty question response.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [WARN] Attempt {attempt} failed: {ex.Message}");
            }
        }

        if (!qSuccess)
        {
            Console.WriteLine($"[ERROR] Failed to generate Question ID {currentId} after 2 attempts. Halting.");
            Environment.Exit(1);
        }
    }

    // ---------------------------------------------------------
    // STEP 2: TECHNICAL & GRAMMAR REVIEW
    // ---------------------------------------------------------
    Console.WriteLine($"\n[2/4] Performing technical accuracy and grammar review on English draft...");

    string reviewPrompt =
        $"You are an expert technical proofreader and senior engineer.\n" +
        $"Review and refine the following JSON array of technical interview questions.\n" +
        $"Verify factual correctness, clarity, grammar, and ensure all strict rules are adhered to:\n" +
        $"- Check that NO backticks (`) are used anywhere (replace them with single quotes '...').\n" +
        $"- AUDIT EXPLANATIONS: Ensure explanations are approximately 5 sentences long and COMPLETELY STANDALONE. Actively strip and rewrite any sentences that mention options or answer keys ('Option A', 'Choice B', 'the correct answer', 'the right option', 'distractors'). The explanation must neutrally describe the underlying mechanism, syntax, or architectural rule that makes this true in general.\n" +
        $"- Ensure answer_win matches the correct answer and strictly follows the cyclic pattern (a, b, c, d...).\n" +
        $"- Maintain the exact question_id, level ('{normalizedLevel}'), and group_index ({gIdx}).\n\n" +
        $"{strictRules}\n\n" +
        $"Questions to review:\n" +
        $"{JsonSerializer.Serialize(enQuestions, writeOptions)}\n\n" +
        $"Return ONLY the updated JSON array without any markdown wrappers.";

    string rawReviewed = await CallAiAsync(reviewPrompt, providerType, apiToken, selectedModel, localApiUrl);
    string cleanedReviewed = CleanJsonString(rawReviewed);

    List<QuestionItem> reviewedEnQuestions = null;
    try
    {
        reviewedEnQuestions = JsonSerializer.Deserialize<List<QuestionItem>>(cleanedReviewed, options);
    }
    catch { /* Fallback to unreviewed if parsing fails */ }

    if (reviewedEnQuestions == null || reviewedEnQuestions.Count == 0)
    {
        Console.WriteLine("  [WARN] Review deserialization failed. Keeping original English batch.");
        reviewedEnQuestions = enQuestions;
    }

    // ---------------------------------------------------------
    // STEP 3: SAVE FINAL ENGLISH DRAFT
    // ---------------------------------------------------------
    await File.WriteAllTextAsync(draftPath, JsonSerializer.Serialize(reviewedEnQuestions, writeOptions));
    Console.WriteLine($"\n[3/4] Intermediate draft finalized: {draftPath}");

    // ---------------------------------------------------------
    // STEP 4: TRANSLATE ONE-BY-ONE INTO 4 LANGUAGES (uk, de, es, fr)
    // ---------------------------------------------------------
    Console.WriteLine($"\n[4/4] Translating questions one by one into 4 languages (uk, de, es, fr)...");
    var completeLevelQuestions = new List<QuestionItem>();

    foreach (var enQuestion in reviewedEnQuestions)
    {
        completeLevelQuestions.Add(enQuestion); // Add the validated English question
        Console.WriteLine($"\n  -> Translating Question ID {enQuestion.QuestionId} into [uk, de, es, fr]...");

        string translatePrompt =
            $"Translate the following single quiz question into exactly 4 languages: 'uk', 'de', 'es', 'fr'.\n" +
            $"Follow these localized topic names for each language:\n{topicGuidelines}\n\n" +
            $"STRICT TRANSLATION RULES:\n" +
            $"- Maintain the EXACT question_id ({enQuestion.QuestionId}), group_index ({gIdx}), level ('{normalizedLevel}'), and answer_win ('{enQuestion.AnswerWin}') across all translations.\n" +
            $"- Use natural, standard technical terminology for each language.\n" +
            $"- NO BACKTICKS: Use single quotes ('...') for code symbols.\n" +
            $"- STANDALONE EXPLANATION: Keep explanation depth (~5 sentences) explaining the concept factually. Do NOT introduce phrases like 'Правильна відповідь:', 'Option A', 'Die richtige Option' in any language.\n\n" +
            $"Source English Question:\n" +
            $"{JsonSerializer.Serialize(enQuestion, writeOptions)}\n\n" +
            $"Return ONLY a JSON array containing the 4 translated objects (one each for uk, de, es, fr).";

        try
        {
            string rawTranslation = await CallAiAsync(translatePrompt, providerType, apiToken, selectedModel, localApiUrl);
            string cleanedTranslation = CleanJsonString(rawTranslation);
            var translatedItems = JsonSerializer.Deserialize<List<QuestionItem>>(cleanedTranslation, options);

            if (translatedItems != null && translatedItems.Count > 0)
            {
                completeLevelQuestions.AddRange(translatedItems);
                Console.WriteLine("    Done translating question!");
            }
            else
            {
                Console.WriteLine("[WARN] Received empty translation array.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Translation failed: {ex.Message}");
        }
    }

    // Save complete level output file
    int width = Math.Max(4, startingId.ToString().Length);
    string paddedId = startingId.ToString("D" + width);
    string levelOutputFileName = $"questions_{gIdx}_{paddedId}_{normalizedLevel.ToLowerInvariant()}.json";
    string levelOutputPath = Path.Combine(directory, levelOutputFileName);

    await File.WriteAllTextAsync(levelOutputPath, JsonSerializer.Serialize(completeLevelQuestions, writeOptions));
    Console.WriteLine($"\n[SUCCESS] Completed level '{normalizedLevel}'!");
    Console.WriteLine($"Saved {completeLevelQuestions.Count} records to: {levelOutputPath}");
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
    string topicsContent = CleanJsonString(await File.ReadAllTextAsync(tPath));

    var options = new JsonSerializerOptions 
    { 
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
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
            string content = CleanJsonString(await File.ReadAllTextAsync(file));
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
        $"- INTERNET SEARCH AND SOURCING REQUIREMENT: Actively perform an internet search across reputable US and European technical resources, official standards (ISO, IEEE, W3C), official documentation, and proven interview archives (e.g., LeetCode, GitHub technical interview collections, cppreference, MDN, Dev.to). Ground questions in real-world interview scenarios and actual industry practices from leading US and EU tech companies.\n" +
        $"- STRICT QUESTION UNIQUENESS: Every question MUST be 100% distinct in concept, scenario, code snippet, and problem statement. Absolutely NO duplicate questions, minor rephrasings, or semantic repetitions across different question_ids, especially within the same group index ({gIdx}) and difficulty level ('{qLevel}'). Each question_id must test a completely different aspect or subtopic.\n" +
        $"- Correct and incorrect answer choices must be of comparable length and complexity so that the correct answer is not obvious.\n" +
        $"- STANDALONE EDUCATIONAL EXPLANATION (CRITICAL): The 'explanation' must be approximately 5 sentences long, explaining the core technical concepts, rules, behavior, and underlying mechanics that determine this outcome.\n" +
        $"- ABSOLUTE BAN ON OPTION/META REFERENCES: The explanation MUST NEVER mention option letters, labels, or meta-references (STRICTLY FORBIDDEN: 'Option A', 'Choice B', 'answer_win', 'the correct option', 'the right answer', 'the incorrect choices', 'distractors'). Explain the subject neutrally and factually as a standalone textbook or documentation reference—explaining HOW and WHY the technology behaves this way in principle, so that any engineer understands the true mechanics without looking at the quiz options.\n" +
        $"- ABSOLUTE PROHIBITION ON BACKTICKS: NEVER use the backtick symbol (`) anywhere in the text. For code elements, function names, types, and keywords (like 'std::vector', 'push_back', 'const', 'int'), ALWAYS use single quotes ('...') or double quotes (\"...\").\n" +
        $"- The correct answer keys MUST follow a simple rotating cycle across consecutive question_ids: a, b, c, d, a, b, c, d...\n\n" +
        $"For EACH unique question_id, you must generate exactly 5 translations (one for each language: en, uk, de, es, fr).\n" +
        $"The questions must be perfectly aligned with the topics defined for this group in each language:\n" +
        $"{topicGuidelines}\n\n" +
        $"CRITICAL JSON OUTPUT FORMAT REQUIREMENTS:\n" +
        $"- Your output MUST be ONLY a single valid JSON array containing all generated question objects.\n" +
        $"- Do NOT include any Markdown formatting, conversational preamble, code block wrappers (like ```json), or trailing notes.\n" +
        $"- Every single object in the array MUST strictly adhere to the exact structure and field names shown below:\n\n" +
        $"[\n" +
        $"  {{\n" +
        $"    \"question_id\": {sId},\n" +
        $"    \"lang\": \"en\",\n" +
        $"    \"level\": \"{qLevel}\",\n" +
        $"    \"group_index\": {gIdx},\n" +
        $"    \"question\": \"Question text here\",\n" +
        $"    \"answer_a\": \"First option\",\n" +
        $"    \"answer_b\": \"Second option\",\n" +
        $"    \"answer_c\": \"Third option\",\n" +
        $"    \"answer_d\": \"Fourth option\",\n" +
        $"    \"answer_win\": \"a\",\n" +
        $"    \"explanation\": \"Detailed standalone educational explanation here (approx. 5 sentences).\"\n" +
        $"  }}\n" +
        $"]\n\n" +
        $"Field Details:\n" +
        $"1. 'question_id' (integer): Identical across all 5 language translations for that question.\n" +
        $"2. 'lang' (string): Must be strictly one of: 'en', 'uk', 'de', 'es', 'fr'.\n" +
        $"3. 'level' (string): Must be exactly '{qLevel}'.\n" +
        $"4. 'group_index' (integer): Must be exactly {gIdx}.\n" +
        $"5. 'question' (string): Must be unique, clear, factual, and accurate.\n" +
        $"6. 'answer_a', 'answer_b', 'answer_c', 'answer_d' (strings): The 4 multiple choice options.\n" +
        $"7. 'answer_win' (string): Exactly 'a', 'b', 'c', or 'd'. Must be identical across all 5 translations for the same question_id and follow the cyclical pattern (a, b, c, d...).\n" +
        $"8. 'explanation' (string): Exactly ~5 sentences of standalone technical mechanics/facts explaining the phenomenon without ever mentioning options, letters, or answer keys.\n\n" +
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
        string processedJson = CleanJsonString(rawTextReceived);

        // Verify JSON before saving
        try
        {
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
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
        string originalJson = CleanJsonString(await File.ReadAllTextAsync(filePath));

        // Strict non-negotiable format rules that ensure Gemini never breaks the output JSON structure
        string strictFormatRules =
            "CRITICAL INSTRUCTIONS FOR OUTPUT FORMAT & EXPLANATIONS:\n" +
            "1. You MUST return ALL questions and ALL translations from the input. Do NOT abbreviate, truncate, or omit any questions, languages, or fields.\n" +
            "2. The output JSON array must contain the exact same number of items as the input, with all fields preserved and only corrected where necessary.\n" +
            "3. AUDIT & REWRITE EXPLANATIONS: Ensure that 'explanation' is an educational, standalone technical reference explaining the underlying concept (~5 sentences). Strip and rephrase ANY references to option letters, answer labels, or choices ('Option A', 'Choice B', 'the correct answer', 'the given option', etc.). The explanation must explain the technical mechanics directly without meta-commentary on the quiz options.\n" +
            "4. NO BACKTICKS: Replace any backticks (`) with single quotes ('...').\n" +
            "5. Output ONLY the updated JSON array. Do NOT write any explanations, conversational filler, greetings, introductions, or markdown block wrapping (like ```json). Return just the raw JSON content.";

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
                "3. Enforce standalone educational explanations: explain WHY the underlying concept/result works mechanically, without referencing option letters or correct/incorrect choices.\n" +
                "4. Correct any errors or inaccuracies you find.\n\n" +
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

        string rawResponse = response.Text.Trim();
        string processedJson = CleanJsonString(rawResponse);

        // Check if the response contains a JSON array
        if (!processedJson.StartsWith("[") || !processedJson.EndsWith("]"))
        {
            // Check if Gemini returned conversational text indicating that everything is already correct
            string lowerText = rawResponse.ToLowerInvariant();
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
            Console.WriteLine(rawResponse);
            Environment.Exit(1);
        }

        // Verify if it is a valid JSON before saving
        try
        {
            var docOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };
            using var doc = JsonDocument.Parse(processedJson, docOptions);
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
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        bool hasErrors = false;

        foreach (var file in filesToProcess)
        {
            try
            {
                string content = CleanJsonString(await File.ReadAllTextAsync(file));
                var questions = JsonSerializer.Deserialize<QuestionItem[]>(content, options);
                if (questions != null)
                {
                    allQuestions.AddRange(questions);
                }
            }
            catch (JsonException ex)
            {
                hasErrors = true;
                Console.WriteLine($"\n[ERROR] Failed to read JSON in file: {Path.GetFileName(file)}");
                Console.WriteLine($"Details: {ex.Message}");
                if (ex.LineNumber.HasValue)
                {
                    Console.WriteLine($"Line: {ex.LineNumber.Value}, Position: {ex.BytePositionInLine}");
                }
            }
        }

        if (hasErrors)
        {
            Console.WriteLine("\nMerging aborted due to JSON errors in one or more files. Please fix them and retry.");
            return;
        }

        if (allQuestions.Count == 0)
        {
            Console.WriteLine("No questions retrieved from files.");
            return;
        }

        // Default language ordering
        var langOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", 1 },
            { "uk", 2 },
            { "de", 3 },
            { "es", 4 },
            { "fr", 5 }
        };

        // Primary sorting by question_id (1, 2, 3...) and fixed language order
        var orderedQuestions = allQuestions
            .GroupBy(q => new { q.GroupIndex, q.QuestionId, Lang = q.Lang.ToLower().Trim() })
            .Select(g => g.First())
            .OrderBy(q => q.QuestionId)
            .ThenBy(q => langOrder.TryGetValue(q.Lang.Trim(), out int ord) ? ord : 99)
            .ThenBy(q => q.GroupIndex)
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
        string jsonContent = CleanJsonString(await File.ReadAllTextAsync(filePath));

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
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
        string jsonContent = CleanJsonString(await File.ReadAllTextAsync(filePath));

        var docOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };
        using var doc = JsonDocument.Parse(jsonContent, docOptions);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
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

// ==========================================
// 8. SORT QUESTIONS BY ID LOGIC
// ==========================================
async Task SortQuestionsByIdAsync(string filePath)
{
    try
    {
        Console.WriteLine($"Reading file for sorting: {filePath}...");
        string jsonContent = CleanJsonString(await File.ReadAllTextAsync(filePath));

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var questions = JsonSerializer.Deserialize<QuestionItem[]>(jsonContent, options);
        if (questions == null || questions.Length == 0)
        {
            Console.WriteLine("Error: File contains no questions or failed to deserialize.");
            return;
        }

        // Language ordering
        var langOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", 1 },
            { "uk", 2 },
            { "de", 3 },
            { "es", 4 },
            { "fr", 5 }
        };

        // Sorting: first by question_id, then by language, then by group_index
        var sortedQuestions = questions
            .OrderBy(q => q.QuestionId)
            .ThenBy(q => langOrder.TryGetValue(q.Lang?.Trim() ?? string.Empty, out int ord) ? ord : 99)
            .ThenBy(q => q.GroupIndex)
            .ToList();

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);

        string outputFileName = $"{fileNameWithoutExt}_sorted{extension}";
        string outputPath = Path.Combine(directory, outputFileName);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string outputJson = JsonSerializer.Serialize(sortedQuestions, writeOptions);
        await File.WriteAllTextAsync(outputPath, outputJson);

        Console.WriteLine($"\nSuccess! Sorted {sortedQuestions.Count} records by question_id.");
        Console.WriteLine($"Output file created: {outputPath}");
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
