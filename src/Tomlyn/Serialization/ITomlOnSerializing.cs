// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked before an instance is serialized to TOML.
/// </summary>
public interface ITomlOnSerializing
{
    /// <summary>
    /// Called before the instance is serialized.
    /// </summary>
    void OnTomlSerializing();
}
