﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GameCheatBehaviour.cs" company="Slash Games">
//   Copyright (c) Slash Games. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Slash.Unity.Common.Diagnostics
{
    using UnityEngine;

    /// <summary>
    ///   Allows games to provide game-specific cheats for the cheat console.
    /// </summary>
    /// <seealso cref="CatchExceptionsBehaviour" />
    public abstract class GameCheatBehaviour : MonoBehaviour
    {
        #region Public Methods and Operators

        /// <summary>
        ///   Renders controls to execute game-specific cheats.
        /// </summary>
        public abstract void DrawCheats();

        #endregion
    }
}