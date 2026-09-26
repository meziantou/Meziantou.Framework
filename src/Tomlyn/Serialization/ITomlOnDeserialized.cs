// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance has been populated during deserialization.
/// </summary>
public interface ITomlOnDeserialized
{
    /// <summary>
    /// Called after the instance has been populated from TOML.
    /// </summary>
    void OnTomlDeserialized();
}
