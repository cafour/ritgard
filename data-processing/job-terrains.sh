#!/bin/bash

#PBS -N ritgard-terrains
#PBS -l select=1:ncpus=20:mem=30gb:scratch_local=20gb
#PBS -l walltime=2:00:00

set -o errexit

if ! command -v dotnet >/dev/null 2>&1 ; then
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --install-dir $SCRATCHDIR/.dotnet --channel 10.0
    export DOTNET_ROOT="$SCRATCHDIR/.dotnet"
    export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
fi

source $HOME/.local/bin/env

cp -r /storage/brno2/home/xstepan1/ritgard $SCRATCHDIR

cd $SCRATCHDIR/ritgard/console

dotnet tool restore
dotnet restore

dataset_dir=${dataset_dir:-'../data-processing/datasets'}
out_dir=${out_dir:-'/storage/brno2/home/xstepan1/out/terrain'}

datasets=${datasets:-$(ls -1 "$dataset_dir" | grep -P '[^\.]+\.json' | grep -v -e 'positions' -e 'terrain' | sed 's/.json//')}

extension='.json'

if [[ "$suffix" != '' ]]; then
    extension=".$suffix.json"
fi

positions_extension='.json'

if [[ "$positions_suffix" != '' ]]; then
    positions_extension=".$positions_suffix.json"
fi

for dataset in $datasets; do
    positions_path="$dataset_dir/$dataset-positions$positions_extension"

    if [[ ! -f $positions_path ]]; then
        echo "Skipping '$positions_path' because it doesn't exist."
        continue
    fi

    args=(dataset_dir/$dataset.json $positions_path)
    
    if [[ "$step_length" != '' ]]; then
        args+=(--step-length-multiplier "$step_length")
    fi

    args+=(--output ./out/$dataset-terrain$positions_extension)

    echo "Computing terrain for '$dataset' with subname '$positions_suffix'"
    dotnet run -c Release -- terrain "${args[@]}" &
    # echo dotnet run -c Release -- terrain "${args[@]}" &

done

wait

cp out/* $out_dir

if [[ "$SCRATCHDIR" != '' ]]; then
    rm -rf $SCRATCHDIR/*
fi

