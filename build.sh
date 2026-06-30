#!/bin/bash
dotnet run ./eng/update-all.cs
dotnet build --configuration Release
