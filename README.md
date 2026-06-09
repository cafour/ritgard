# Ritgard

Ritgard is a tool for visualizing GitHub's socio-technical artifacts (STAs): Issues, Pull Requests, and Discussions.

## Dependencies

Before running the tools or its scripts, make sure the following is available on your machine:

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [The `uv` Python package manager](https://docs.astral.sh/uv/)
- Hardware suitable for the embedding model you plan to use (i.e., GPU with enough VRAM)
- Enough space on disk (8 GB of Python dependencies and the size of the embedding model)
- Access to a Large Language Model (LLM) through an OpenAI-compatible REST API (if you desire human-friendly topic labels)

## Building and running

The tool has a three-stage pipeline:

1. **Data mining** using `Ritgard.Console`'s `repo` command.
2. **Data processing** using Python scripts in `data-processing/`.
3. **Visualization** with the Godot-based visualizer in `vis/`.

### 1. Data mining

1. Restore .NET tools with `dotnet tool restore`.
2. Restore NuGet dependencies with `dotnet restore`.
3. To build the command-line utilities, including the data miner, run [`dotnet publish`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) while in `console/`.
4. Obtain your [GitHub fine-grained personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens). No scopes are required for the data mining of public repositories. Put the token into the `GitHubTokens__Main__Token` environment variable or into the miner's `appsettings.json`.
5. To mine the STAs of a GitHub repository, run the following command while in the directory where you have previously published the miner:
  ```bash
  ./Ritgard.Console repo "${owner}/${name}" \
     --scope "${mining_scope}" \
     --output "${data_path}"
  ```
  Where `${owner}` is the owner account of the repository, `${name}` is its name, and `${mining_scope}` describes which STAs to mine (set to `All` by default).
  
### 2. Data processing

1. Install Python dependencies. While in `data-processing/` run:
  ```bash
  uv sync \
    --preview-features extra-build-dependencies \
    --no-build-isolation-package flash-attn
  ```
  However, `flash-attn` requires CUDA. If you do not plan on using it, run:
  ```bash
  uv sync --no-install-package flash-attn
  ```
2. If you plan on using an LLM to generate topic names, put the LLM's API endpoint and your API key into the `LLM_BASE_URL` and `LLM_API_KEY` environment variables respectively.
3. To model the topics of the mined STAs, run the following command while in the `data-processing/` directory:
  ```bash
  uv run ./model-topics.py \
     --llm \
     --embed-labels \
     --embed-bodies \
     --embed-comments \
     --embed-model "${embed_model}" \
     --embed-batch-size "${embed_batch_size}" \
     --min-cluster-size "${min_cluster_size}" \
     --min-samples "${min_samples}" \
     --flash-attention \
     --output-path "${topics_path}" \
     "${data_path}
  ```
  Where `${data_path}` is a path to the JSON file generated in the previous step with `Ritgard.Console`. Set the other variables according to your preference or leave them out to use default values. To disable LLM-generated labels, remove the `--llm` flag. Please note that the [`--flash-attention`](https://github.com/Dao-AILab/flash-attention) is optional and available only on Linux.
7. To compute the map scale, prevent tree overlaps, and adjust the STA positions accordingly, run the following command while in the `data-processing/` directory:
  ```bash
  uv run ./adjust-positions.py \
     --world-sizing "${world_sizing}" \
     --world-sizing-auto-quantile \
     "${auto_quantile}" \
     --data-path "${data_path}" \
     --output "${positions_path}" \
     ${topics_path}
  ```
8. Optionally, precompute island heightmaps. In the miner's directory, run:
  ```bash
  ./Ritgard.Console terrain \
      "${data_path}" \
      "${positions_path}" \
      --step-length-multiplier "${step_length}" \
      --scope "${terrain_scope}" \
      --sliding-window-presets \
        "${sliding_windows}" \
      --batch-size "${batch_size}" \
      --output "${terrain_path}"
  ```
  Where `${step_length}` is the length of the short step (press of the ',' or '.' key) in days. `${terrain_scope}` and `${sliding_windows}` can be used to limit which terrain get generated.

### 3. Visualization

1. To build the visualization tool, open `vis/` in the Godot editor and [export the project](https://docs.godotengine.org/en/4.6/tutorials/export/).
2. In `appsettings.json` of the exported build, set the `DataPath` property to the directory with the datasets.
3. Copy or move the final `${data_path}`, `${positions_path}`, and `${terrain_path}` files to the configured dataset directory. The filenames must be in the following format:
  ```bash
  data_path=${name}.json
  positions_path=${name}-positions.${subname}.json
  terrain_path=${name}-terrain.${subname}.json
  ```
  Where `${subname}` is optional and appears in the tool in the dataset dropdown as _"name (subname)"_.
4. Run the built tool.
