// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance is created but before it is populated during deserialization.
/// </summary>
public interface ITomlOnDeserializing
{
    /// <summary>
    /// Called before the instance is populated from TOML.
    /// </summary>
    void OnTomlDeserializing();
}
