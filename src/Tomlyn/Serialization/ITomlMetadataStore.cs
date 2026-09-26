// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Tomlyn.Model;

namespace Tomlyn.Serialization;

/// <summary>
/// Associates TOML trivia/comment metadata with object instances.
/// </summary>
public interface ITomlMetadataStore
{
    /// <summary>
    /// Tries to get metadata for an instance.
    /// </summary>
    bool TryGetProperties(object instance, out TomlPropertiesMetadata? metadata);

    /// <summary>
    /// Sets metadata for an instance.
    /// </summary>
    void SetProperties(object instance, TomlPropertiesMetadata? metadata);
}

