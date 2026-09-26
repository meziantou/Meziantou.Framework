// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance has been serialized to TOML.
/// </summary>
public interface ITomlOnSerialized
{
    /// <summary>
    /// Called after the instance has been serialized.
    /// </summary>
    void OnTomlSerialized();
}
