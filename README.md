# Ritgard

Ritgard is a tool for visualizing GitHub's socio-technical artifacts (STAs): Issues, Pull Requests, and Discussions.

## Building and running

Ritgard has a three-stage pipeline:

1. **Data mining** using `Ritgard.Console`'s `repo` command.
2. **Data processing** using Python scripts in `data-processing/`.
3. **Visualization** with the Godot-based visualizer in `vis/`.

Each stage has requirements that may require you to use different machines. Ensure you have the following available:

**Data mining**
- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- `git` (accessible from the `Path` variable)
- [`cloc`](https://github.com/aldanial/cloc) (accessible from the `Path` variable)

**Data processing**
- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [The `uv` Python package manager](https://docs.astral.sh/uv/)
- Hardware suitable for the embedding model you plan to use (i.e., GPU with enough VRAM)
- Enough space on disk (8 GB of Python dependencies and the size of the embedding model)
- Access to a large language model (LLM) through an OpenAI-compatible REST API

**Visualization**
- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Godot 4.6.3 with .NET support](https://godotengine.org/download/archive/4.6.3-stable/)
- Hardware suitable for rendering a large amount of meshes (i.e., a dedicated GPU)

### 1. Data mining

1. Restore .NET tools with `dotnet tool restore` (note the `tool` argument).
2. Restore NuGet dependencies with `dotnet restore`.
3. Navigate to `console/`.
4. To build the command-line interface (CLI) to the data miner and the terrain generator, run:
    ```bash
    dotnet publish --output `${console_dir}`
    ```
    Where `${console_dir}` is a directory of your choosing.
5. Obtain your [GitHub fine-grained personal access token#creating-a-fine-grained-personal-access-token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens). No Permissions are required for the data mining of public repositories. Put the token into the `GitHubTokens__Main__Token` environment variable or into the CLI's `./appsettings.json`.
6. Navigate to `${console_dir}`.
7. To mine the STAs of a GitHub repository, run the following command while in the directory where you have previously published the miner:
    ```bash
    ./Ritgard.Console repo "${owner}/${name}" --output "${data_path}"
    ```
    Where `${owner}` is the owner account of the repository and `${name}`.
  
### 2. Data processing

> For a complete list of options and their possible values, run each script with the `--help` flag.

1. Navigate to `data-processing/`
2. Install Python dependencies. While in `data-processing/` run:
    ```bash
    uv sync \
      --preview-features extra-build-dependencies \
      --no-build-isolation-package flash-attn
    ```
    However, [`flash-attn`](https://github.com/Dao-AILab/flash-attention) requires CUDA and only works on Linux, so you may want to omit it with:
    ```bash
    uv sync --no-install-package flash-attn
    ```
3. If you plan on using an LLM to generate topic names, put the LLM's API endpoint and your API key into the `LLM_BASE_URL` and `LLM_API_KEY` environment variables respectively.
4. To model the topics of the mined STAs, run the following command while in the `data-processing/` directory:
    ```bash
    uv run ./model-topics.py \
      --llm \
      --flash-attention \
      --embed-labels \
      --embed-bodies \
      --embed-comments \
      --embed-model "${embed_model}" \
      --embed-batch-size "${embed_batch_size}" \
      --min-cluster-size "${min_cluster_size}" \
      --min-samples "${min_samples}" \
      --output-path "${topics_path}" \
      "${data_path}
    ```
    Where:
    - `--llm` controls where an LLM will be used to create topic names (requires setting the `LLM_*` environment variables),
    - `--flash-attention` can only be used if you installed the `flash-attn` package,
    - `${embed_model}` is the embedding model to be used (`Qwen/Qwen3-Embedding-8B` by default),
    - `${data_path}` is the path to the data you mined previously,
    - `${topics_path}` is where the modeled topics will be stored in JSON,
    - the rest of the variables is set according to your preference.
5. To compute the map scale, prevent tree overlaps, and adjust the STA positions accordingly, run the following command while in the `data-processing/` directory:
    ```bash
    uv run ./adjust-positions.py \
      --world-sizing "${world_sizing}" \
      --world-sizing-auto-quantile \
      "${auto_quantile}" \
      --data-path "${data_path}" \
      --output "${positions_path}" \
      ${topics_path}
    ```
    Where:
    - `${data_path}` is the path to the mined data,
    - `${topics_path}` is the output from `model-topics.py`,
    - `${positions_path}` is where the computed STA positions will be stored in JSON, and
    - the rest of the variables depends on your preference.
6. Optionally, precompute the island terrain to avoid staggering during runtime. Follow steps 7-8:
7. Navigate to `${console_dir}`.
8. To generate island terrain, run:
    ```bash
    ./Ritgard.Console terrain \
        "${data_path}" \
        "${positions_path}" \
        --step-length-multiplier "${step_length}" \
        --scope "${terrain_scope}" \
        --sliding-window-presets \
          "${sliding_windows}" \
        --batch-size "${terrain_batch_size}" \
        --output "${terrain_path}"
    ```
    Where:
      - `${data_path}` is a file containing the mined data,
      - `${positions_path}` is the output of `adjust-positions.py`,
      - `${terrain_path}` is where the output terrain will be stored in JSON,
      - `${step_length}` variable is the length of the short step in days, and
      - `${terrain_batch_size}` can be used to prevent out-of-memory exceptions by chunking the heightmap data,
      - the `${terrain_scope}` and `${sliding_windows}` variable can be used to limit which terrains get generated.

### 3. Visualization

1. To build the visualization tool, open `vis/` in the Godot editor and [export the project](https://docs.godotengine.org/en/4.6/tutorials/export/). Please note that the version of the used export templates must match the version of your Godot editor.
2. In `appsettings.json` of the exported build, set the `DataPath` property to the directory with the datasets.
3. Copy or move the final `${data_path}`, `${positions_path}`, and `${terrain_path}` files to the configured dataset directory. The filenames must be in the following format:
    ```bash
    data_path=${name}.json
    positions_path=${name}-positions.${subname}.json
    terrain_path=${name}-terrain.${subname}.json
    ```
    Where `${subname}` is optional and appears in the tool in the dataset dropdown as _"name (subname)"_.
    > The output of `./model-topics.json` (`${topics_path}`) is not required here, as it is contained within `${positions_path}`.
4. Run the built tool.
