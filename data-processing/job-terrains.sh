#!/bin/bash

#PBS -N ritgard-terrains
#PBS -l select=1:ncpus=16:mem=80gb:scratch_local=20gb
#PBS -l walltime=4:00:00

set -o errexit

if ! command -v dotnet >/dev/null 2>&1 ; then
    curl -sSL https://dot.net/v1/dotnet-install.sh > $SCRATCHDIR/dotnet-install.sh
    bash $SCRATCHDIR/dotnet-install.sh --install-dir $SCRATCHDIR/.dotnet --channel 10.0
    bash $SCRATCHDIR/dotnet-install.sh --install-dir $SCRATCHDIR/.dotnet --channel 9.0 --skip-non-versioned-files
    export DOTNET_ROOT="$SCRATCHDIR/.dotnet"
    export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
fi

source $HOME/.local/bin/env

cp -r /storage/brno2/home/xstepan1/ritgard $SCRATCHDIR

cd $SCRATCHDIR/ritgard/console

dotnet tool restore
dotnet restore
dotnet build -c Release

dataset_dir=${dataset_dir:-'../data-processing/datasets'}
out_dir=${out_dir:-'/storage/brno2/home/xstepan1/out/terrains'}

datasets=${datasets:-$(ls -1 "$dataset_dir" | grep -P '[^\.]+\.json' | grep -v -e 'positions' -e 'terrain' | sed 's/.json//')}

positions_extension='.json'

if [[ "$positions_suffix" != '' ]]; then
    positions_extension=".$positions_suffix.json"
fi

extension='.json'

if [[ "$suffix" == '' ]]; then
    suffix="$positions_suffix"
fi

if [[ "$suffix" != '' ]]; then
    extension=".$suffix.json"
fi

mkdir -p out

for dataset in $datasets; do
    positions_path="$dataset_dir/$dataset-positions$positions_extension"

    if [[ ! -f $positions_path ]]; then
        echo "Skipping '$positions_path' because it doesn't exist."
        continue
    fi

    args=("$dataset_dir/$dataset.json" "$positions_path")
    
    if [[ "$step_length" != '' ]]; then
        args+=(--step-length-multiplier "$step_length")
    fi

    if [[ "$batch_size" != '' ]]; then
        args+=(--batch-size "$batch_size")
    fi
    
    if [[ "$scope" != '' ]]; then
        args+=(--scope "$scope")
    fi

    if [[ "$sliding_windows" != '' ]]; then
        args+=(--sliding-window-presets "$sliding_windows")
    fi

    out_filename=$dataset-terrain$extension

    if [[ -f "$out_dir/$out_filename" ]]; then
        echo "Skipping '$out_filename' because it already exists."
        continue
    fi

    out_path=./out/$out_filename
    args+=(--output "$out_path")

    echo "Computing terrain for '$dataset' with subname '$positions_suffix'"
    (dotnet run -c Release --no-build -- terrain "${args[@]}" ; cp "$out_path" "$out_dir/$out_filename") &
    # echo dotnet run -c Release -- terrain "${args[@]}" &

done

wait

# cp out/* $out_dir

if [[ "$SCRATCHDIR" != '' ]]; then
    rm -rf $SCRATCHDIR/*
fi

