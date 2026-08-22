# GeminiChecker CLI Usage Examples

All four operational modes of the GeminiChecker utility are executed through a unified command-line interface. The application automatically detects the appropriate mode based on the provided flags and path types.

---

## 1. Splitting a Large File (Split Mode)
Slices a large source JSON file containing quiz questions into smaller chunk files of 8 unique questions (keeping all language translations grouped together for each ID). The program automatically analyzes the maximum question ID in the file and pads the filename indexes with leading zeros for neat organization.

# Basic usage (pass only the full path to the source JSON file)
dotnet run -- "C: \Work\questions.json"
Result: Chunk files like questions_0_0001.json, questions_0_0009.json, questions_0_0017.json, etc., will be automatically generated in the same directory.
2. Verifying and Correcting Files with Gemini (Check Mode)
Sends a specific chunk file directly to Google Gemini to review and correct grammar across all 5 languages, verify Java code execution, technical logic, and explanations. The corrected output is saved to a new file with the _chn suffix (e.g., questions_0_0001_chn.json).

# Verify a file by passing your Google AI Studio API key
dotnet run -- "C: \Work\questions_0_0001.json" --check --key "AI_STUDIO_API_KEY"

# Verify using a custom AI guidelines file (-p) and a specific model (-m)
dotnet run -- "C: \Work\questions_0_0001.json" -c -k "AI_STUDIO_API_KEY" -p "C: \Work\java_rules.txt" -m "gemini-3.6-flash"
3. Merging Chunk Files & Stats Analysis (Merge & Stats Mode)
Scans and aggregates all small chunk files inside the specified directory. If a file has a version with the _chn.json suffix, the original file is ignored and the corrected version is processed instead. All data is automatically sorted, de-duplicated, merged into a single master file questions_yyyy-MM-dd_HH-mm-ss.json, and comprehensive statistics are printed.

# Merge all chunk files in a folder (pass only the directory path)
dotnet run -- "C: \Work\"
4. Generating New Questions with Gemini (Generate Mode)
Generates new, unique quiz questions using AI. The program dynamically loads the topic name from topics.json for the specified group index in all 

5 languages, automatically scans the directory for existing questions to compile a blacklist (strictly preventing duplicates), and creates a new chunk file with proper padding.

# Generate 8 new junior-level questions for group 0, starting with ID 513
dotnet run -- --generate --topics "C: \Work\topics.json" --group 0 --count 8 --level junior --start-id 513 --key "AI_STUDIO_API_KEY"

# Generate using custom guidelines loaded from an external text file (-p)
dotnet run -- -gen -t "C: \Work\topics.json" -g 0 -cnt 8 -l middle -s 513 -k "AI_STUDIO_API_KEY" -p "C: \Work\python_rules.txt"
Command Line Options & Flags:
-gen, --generate Enables AI-powered question generation mode.
-c, --check Enables AI verification and correction mode on the specified file.
-t, --topics <path> Path to the topics specification file (topics.json).
-g, -grp, --group <idx> Target group index (topic) for generation (e.g., 0).
-cnt, --count <int> Number of unique questions to generate (default: 8).
-l, --level <string> Difficulty level (e.g., junior, middle, senior).
-s, --start-id <int> Starting question_id for newly generated questions.
-k, --key <api_key> Google AI Studio API Key (if omitted, falls back to GEMINI_API_KEY environment variable).
-m, --model <model_name> Selects the Gemini model (default: gemini-3.6-flash).
-p, --prompt <file_path> Path to a text file containing custom guidelines for the AI (works in both verification and generation modes).