using System.Globalization;
using System.Resources;

namespace Ironmon.Updater.Core;

/// <summary>
/// Resolves installer and updater messages from the shared culture-aware resource catalog.
/// </summary>
public static class UpdaterText
{
    private const string ResourceName = "Ironmon.Updater.Core.Resources.Localization.UpdaterResources";
    private static readonly ResourceManager _resources = new(ResourceName, typeof(UpdaterText).Assembly);

    /// <summary>
    /// Retrieves a required resource using the current UI culture and the neutral English fallback.
    /// </summary>
    /// <param name="key">The stable resource identity.</param>
    /// <returns>The localized text.</returns>
    private static string Get(string key)
        => _resources.GetString(key, CultureInfo.CurrentUICulture) ?? throw new MissingManifestResourceException(key);

    /// <summary>
    /// Gets the localized text: A new game installation requires an empty destination.
    /// </summary>
    public static string CombinedGamePreparationANewGameInstallationRequiresAnEmptyDestination
        => Get(nameof(CombinedGamePreparationANewGameInstallationRequiresAnEmptyDestination));

    /// <summary>
    /// Gets the localized text: Game preparation and private Git must remain outside the installation.
    /// </summary>
    public static string CombinedGamePreparationGamePreparationAndPrivateGitMustRemainOutsideThe
        => Get(nameof(CombinedGamePreparationGamePreparationAndPrivateGitMustRemainOutsideThe));

    /// <summary>
    /// Gets the localized text: The exact approved game revision could not be fetched or verified as a forward update. The installed game remains unchanged.
    /// </summary>
    public static string CombinedGamePreparationTheExactApprovedGameRevisionCouldNotBeFetched
        => Get(nameof(CombinedGamePreparationTheExactApprovedGameRevisionCouldNotBeFetched));

    /// <summary>
    /// Gets the localized text: The game tree contains links, submodules or unsupported objects.
    /// </summary>
    public static string CombinedGamePreparationTheGameTreeContainsLinksSubmodulesOrUnsupportedObjects
        => Get(nameof(CombinedGamePreparationTheGameTreeContainsLinksSubmodulesOrUnsupportedObjects));

    /// <summary>
    /// Gets the localized text: The game was changed externally or is already newer; an automatic downgrade is not permitted.
    /// </summary>
    public static string CombinedGamePreparationTheGameWasChangedExternallyOrIsAlreadyNewer
        => Get(nameof(CombinedGamePreparationTheGameWasChangedExternallyOrIsAlreadyNewer));

    /// <summary>
    /// Gets the localized text: The installation changed while the combined update was being prepared.
    /// </summary>
    public static string CombinedGamePreparationTheInstallationChangedWhileTheCombinedUpdateWasBeing
        => Get(nameof(CombinedGamePreparationTheInstallationChangedWhileTheCombinedUpdateWasBeing));

    /// <summary>
    /// Gets the localized text: The signed game identity must resolve to an exact commit object.
    /// </summary>
    public static string CombinedGamePreparationTheSignedGameIdentityMustResolveToAnExact
        => Get(nameof(CombinedGamePreparationTheSignedGameIdentityMustResolveToAnExact));

    /// <summary>
    /// Gets the localized text: The signed game inventory does not cover the complete fetched commit.
    /// </summary>
    public static string CombinedGamePreparationTheSignedGameInventoryDoesNotCoverTheComplete
        => Get(nameof(CombinedGamePreparationTheSignedGameInventoryDoesNotCoverTheComplete));

    /// <summary>
    /// Gets the localized text: Combined updates cannot downgrade or cross unrelated game histories.
    /// </summary>
    public static string CombinedGitVerificationCombinedUpdatesCannotDowngradeOrCrossUnrelatedGameHistories
        => Get(nameof(CombinedGitVerificationCombinedUpdatesCannotDowngradeOrCrossUnrelatedGameHistories));

    /// <summary>
    /// Gets the localized text: Combined updates require prepared Git metadata.
    /// </summary>
    public static string CombinedGitVerificationCombinedUpdatesRequirePreparedGitMetadata
        => Get(nameof(CombinedGitVerificationCombinedUpdatesRequirePreparedGitMetadata));

    /// <summary>
    /// Gets the localized text: The installed Git branch and index do not match the approved game revision.
    /// </summary>
    public static string CombinedGitVerificationTheInstalledGitBranchAndIndexDoNotMatch
        => Get(nameof(CombinedGitVerificationTheInstalledGitBranchAndIndexDoNotMatch));

    /// <summary>
    /// Gets the localized text: The prepared branch or index does not identify the signed game target.
    /// </summary>
    public static string CombinedGitVerificationThePreparedBranchOrIndexDoesNotIdentifyThe
        => Get(nameof(CombinedGitVerificationThePreparedBranchOrIndexDoesNotIdentifyThe));

    /// <summary>
    /// Gets the localized text: The transaction&apos;s Git state differs from its independently verified recovery evidence.
    /// </summary>
    public static string CombinedGitVerificationTheTransactionSGitStateDiffersFromItsIndependently
        => Get(nameof(CombinedGitVerificationTheTransactionSGitStateDiffersFromItsIndependently));

    /// <summary>
    /// Gets the localized text: This installation path is too long for safe game recovery. Choose a shorter folder path before installing.
    /// </summary>
    public static string CombinedGitVerificationThisInstallationPathIsTooLongForSafeGame
        => Get(nameof(CombinedGitVerificationThisInstallationPathIsTooLongForSafeGame));

    /// <summary>
    /// Gets the localized text: This transaction already has Git verification evidence.
    /// </summary>
    public static string CombinedGitVerificationThisTransactionAlreadyHasGitVerificationEvidence
        => Get(nameof(CombinedGitVerificationThisTransactionAlreadyHasGitVerificationEvidence));

    /// <summary>
    /// Gets the localized text: Another launcher changed the game repository during preparation.
    /// </summary>
    public static string CombinedUpdateAnotherLauncherChangedTheGameRepositoryDuringPreparation
        => Get(nameof(CombinedUpdateAnotherLauncherChangedTheGameRepositoryDuringPreparation));

    /// <summary>
    /// Gets the localized text: Combined update downloads must remain outside the installed game.
    /// </summary>
    public static string CombinedUpdateCombinedUpdateDownloadsMustRemainOutsideTheInstalledGame
        => Get(nameof(CombinedUpdateCombinedUpdateDownloadsMustRemainOutsideTheInstalledGame));

    /// <summary>
    /// Gets the localized text: Finish the active run before updating the game. Retained generation data does not guarantee save compatibility.
    /// </summary>
    public static string CombinedUpdateFinishTheActiveRunBeforeUpdatingTheGameRetained
        => Get(nameof(CombinedUpdateFinishTheActiveRunBeforeUpdatingTheGameRetained));

    /// <summary>
    /// Gets the localized text: The combined recovery evidence exceeds its supported size.
    /// </summary>
    public static string CombinedUpdateTheCombinedRecoveryEvidenceExceedsItsSupportedSize
        => Get(nameof(CombinedUpdateTheCombinedRecoveryEvidenceExceedsItsSupportedSize));

    /// <summary>
    /// Gets the localized text: A game file overlaps an Ironmon-owned destination.
    /// </summary>
    public static string FileManagementPolicyAGameFileOverlapsAnIronmonOwnedDestination
        => Get(nameof(FileManagementPolicyAGameFileOverlapsAnIronmonOwnedDestination));

    /// <summary>
    /// Gets the localized text: Historical generation profiles require immutable file ownership.
    /// </summary>
    public static string FileManagementPolicyHistoricalGenerationProfilesRequireImmutableFileOwnership
        => Get(nameof(FileManagementPolicyHistoricalGenerationProfilesRequireImmutableFileOwnership));

    /// <summary>
    /// Gets the localized text: Immutable files must specify one exact byte representation.
    /// </summary>
    public static string FileManagementPolicyImmutableFilesMustSpecifyOneExactByteRepresentation
        => Get(nameof(FileManagementPolicyImmutableFilesMustSpecifyOneExactByteRepresentation));

    /// <summary>
    /// Gets the localized text: The inventory contains an unsupported ownership policy.
    /// </summary>
    public static string FileManagementPolicyTheInventoryContainsAnUnsupportedOwnershipPolicy
        => Get(nameof(FileManagementPolicyTheInventoryContainsAnUnsupportedOwnershipPolicy));

    /// <summary>
    /// Gets the localized text: The Ironmon inventory claims a file outside its managed destinations.
    /// </summary>
    public static string FileManagementPolicyTheIronmonInventoryClaimsAFileOutsideItsManaged
        => Get(nameof(FileManagementPolicyTheIronmonInventoryClaimsAFileOutsideItsManaged));

    /// <summary>
    /// Gets the localized text: The release inventory overlaps protected user or updater data at &apos;{0}&apos;.
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string FileManagementPolicyTheReleaseInventoryOverlapsProtectedUserOrUpdaterData(object? value0)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(FileManagementPolicyTheReleaseInventoryOverlapsProtectedUserOrUpdaterData)), value0);

    /// <summary>
    /// Gets the localized text: A release attempts to change an immutable file under its existing identity.
    /// </summary>
    public static string FileUpdatePlannerAReleaseAttemptsToChangeAnImmutableFileUnder
        => Get(nameof(FileUpdatePlannerAReleaseAttemptsToChangeAnImmutableFileUnder));

    /// <summary>
    /// Gets the localized text: Case-only path changes require a separate supported rename policy.
    /// </summary>
    public static string FileUpdatePlannerCaseOnlyPathChangesRequireASeparateSupportedRename
        => Get(nameof(FileUpdatePlannerCaseOnlyPathChangesRequireASeparateSupportedRename));

    /// <summary>
    /// Gets the localized text: Changing file ownership or retention policy requires an explicit migration policy.
    /// </summary>
    public static string FileUpdatePlannerChangingFileOwnershipOrRetentionPolicyRequiresAnExplicit
        => Get(nameof(FileUpdatePlannerChangingFileOwnershipOrRetentionPolicyRequiresAnExplicit));

    /// <summary>
    /// Gets the localized text: The installation snapshot has missing directories or a file/directory collision.
    /// </summary>
    public static string FileUpdatePlannerTheInstallationSnapshotHasMissingDirectoriesOrAFile
        => Get(nameof(FileUpdatePlannerTheInstallationSnapshotHasMissingDirectoriesOrAFile));

    /// <summary>
    /// Gets the localized text: The installation snapshot is too large or contains duplicate paths.
    /// </summary>
    public static string FileUpdatePlannerTheInstallationSnapshotIsTooLargeOrContainsDuplicate
        => Get(nameof(FileUpdatePlannerTheInstallationSnapshotIsTooLargeOrContainsDuplicate));

    /// <summary>
    /// Gets the localized text: The inventory is too large or claims a path more than once.
    /// </summary>
    public static string FileUpdatePlannerTheInventoryIsTooLargeOrClaimsAPath
        => Get(nameof(FileUpdatePlannerTheInventoryIsTooLargeOrClaimsAPath));

    /// <summary>
    /// Gets the localized text: The inventory uses one path as both a file and a directory.
    /// </summary>
    public static string FileUpdatePlannerTheInventoryUsesOnePathAsBothAFile
        => Get(nameof(FileUpdatePlannerTheInventoryUsesOnePathAsBothAFile));

    /// <summary>
    /// Gets the localized text: Only an explicitly listed replaceable file conflict can be approved.
    /// </summary>
    public static string FileUpdatePlanOnlyAnExplicitlyListedReplaceableFileConflictCanBe
        => Get(nameof(FileUpdatePlanOnlyAnExplicitlyListedReplaceableFileConflictCanBe));

    /// <summary>
    /// Gets the localized text: Unresolved file conflicts block the complete update.
    /// </summary>
    public static string FileUpdatePlanUnresolvedFileConflictsBlockTheCompleteUpdate
        => Get(nameof(FileUpdatePlanUnresolvedFileConflictsBlockTheCompleteUpdate));

    /// <summary>
    /// Gets the localized text: An update is incomplete. Finish or recover it before starting Ironmon.
    /// </summary>
    public static string GameCompatibilityCheckAnUpdateIsIncompleteFinishOrRecoverItBefore
        => Get(nameof(GameCompatibilityCheckAnUpdateIsIncompleteFinishOrRecoverItBefore));

    /// <summary>
    /// Gets the localized text: A required game file is missing.
    /// </summary>
    public static string GameCompatibilityCheckARequiredGameFileIsMissing
        => Get(nameof(GameCompatibilityCheckARequiredGameFileIsMissing));

    /// <summary>
    /// Gets the localized text: Installed game code or data differs from this Ironmon release.
    /// </summary>
    public static string GameCompatibilityCheckInstalledGameCodeOrDataDiffersFromThisIronmon
        => Get(nameof(GameCompatibilityCheckInstalledGameCodeOrDataDiffersFromThisIronmon));

    /// <summary>
    /// Gets the localized text: Linked Git layouts are unsupported.
    /// </summary>
    public static string GameCompatibilityCheckLinkedGitLayoutsAreUnsupported
        => Get(nameof(GameCompatibilityCheckLinkedGitLayoutsAreUnsupported));

    /// <summary>
    /// Gets the localized text: The game changed during inspection. Check the installation again.
    /// </summary>
    public static string GameCompatibilityCheckTheGameChangedDuringInspectionCheckTheInstallationAgain
        => Get(nameof(GameCompatibilityCheckTheGameChangedDuringInspectionCheckTheInstallationAgain));

    /// <summary>
    /// Gets the localized text: The game compatibility baseline is empty.
    /// </summary>
    public static string GameCompatibilityCheckTheGameCompatibilityBaselineIsEmpty
        => Get(nameof(GameCompatibilityCheckTheGameCompatibilityBaselineIsEmpty));

    /// <summary>
    /// Gets the localized text: The game HEAD reference is missing.
    /// </summary>
    public static string GameCompatibilityCheckTheGameHEADReferenceIsMissing
        => Get(nameof(GameCompatibilityCheckTheGameHEADReferenceIsMissing));

    /// <summary>
    /// Gets the localized text: The game HEAD reference is unsupported.
    /// </summary>
    public static string GameCompatibilityCheckTheGameHEADReferenceIsUnsupported
        => Get(nameof(GameCompatibilityCheckTheGameHEADReferenceIsUnsupported));

    /// <summary>
    /// Gets the localized text: The game matches this Ironmon release.
    /// </summary>
    public static string GameCompatibilityCheckTheGameMatchesThisIronmonRelease
        => Get(nameof(GameCompatibilityCheckTheGameMatchesThisIronmonRelease));

    /// <summary>
    /// Gets the localized text: The game was changed outside Ironmon. A matching Ironmon release is required.
    /// </summary>
    public static string GameCompatibilityCheckTheGameWasChangedOutsideIronmonAMatchingIronmon
        => Get(nameof(GameCompatibilityCheckTheGameWasChangedOutsideIronmonAMatchingIronmon));

    /// <summary>
    /// Gets the localized text: The Git reference exceeds its size limit.
    /// </summary>
    public static string GameCompatibilityCheckTheGitReferenceExceedsItsSizeLimit
        => Get(nameof(GameCompatibilityCheckTheGitReferenceExceedsItsSizeLimit));

    /// <summary>
    /// Gets the localized text: The installed game could not be verified. Check the installation before starting Ironmon.
    /// </summary>
    public static string GameCompatibilityCheckTheInstalledGameCouldNotBeVerifiedCheckThe
        => Get(nameof(GameCompatibilityCheckTheInstalledGameCouldNotBeVerifiedCheckThe));

    /// <summary>
    /// Gets the localized text: A folder containing Git metadata cannot be adopted as a ZIP installation.
    /// </summary>
    public static string GameDirectorySnapshotAFolderContainingGitMetadataCannotBeAdoptedAs
        => Get(nameof(GameDirectorySnapshotAFolderContainingGitMetadataCannotBeAdoptedAs));

    /// <summary>
    /// Gets the localized text: The installation has too many entries for automatic preparation.
    /// </summary>
    public static string GameDirectorySnapshotTheInstallationHasTooManyEntriesForAutomaticPreparation
        => Get(nameof(GameDirectorySnapshotTheInstallationHasTooManyEntriesForAutomaticPreparation));

    /// <summary>
    /// Gets the localized text: No supported game was found above the tracker.
    /// </summary>
    public static string GameInstallationLocatorNoSupportedGameWasFoundAboveTheTracker
        => Get(nameof(GameInstallationLocatorNoSupportedGameWasFoundAboveTheTracker));

    /// <summary>
    /// Gets the localized text: The folder does not contain the expected game executable and INI.
    /// </summary>
    public static string GameInstallationLocatorTheFolderDoesNotContainTheExpectedGameExecutable
        => Get(nameof(GameInstallationLocatorTheFolderDoesNotContainTheExpectedGameExecutable));

    /// <summary>
    /// Gets the localized text: The game executable has an unsupported header.
    /// </summary>
    public static string GameInstallationLocatorTheGameExecutableHasAnUnsupportedHeader
        => Get(nameof(GameInstallationLocatorTheGameExecutableHasAnUnsupportedHeader));

    /// <summary>
    /// Gets the localized text: The game INI does not identify the supported Infinite Fusion game.
    /// </summary>
    public static string GameInstallationLocatorTheGameINIDoesNotIdentifyTheSupportedInfinite
        => Get(nameof(GameInstallationLocatorTheGameINIDoesNotIdentifyTheSupportedInfinite));

    /// <summary>
    /// Gets the localized text: The game INI has duplicate game sections.
    /// </summary>
    public static string GameInstallationLocatorTheGameINIHasDuplicateGameSections
        => Get(nameof(GameInstallationLocatorTheGameINIHasDuplicateGameSections));

    /// <summary>
    /// Gets the localized text: The game INI has duplicate titles.
    /// </summary>
    public static string GameInstallationLocatorTheGameINIHasDuplicateTitles
        => Get(nameof(GameInstallationLocatorTheGameINIHasDuplicateTitles));

    /// <summary>
    /// Gets the localized text: The installed tracker executable was not found.
    /// </summary>
    public static string GameInstallationLocatorTheInstalledTrackerExecutableWasNotFound
        => Get(nameof(GameInstallationLocatorTheInstalledTrackerExecutableWasNotFound));

    /// <summary>
    /// Gets the localized text: Git output exceeded the supported size.
    /// </summary>
    public static string GitProcessGitOutputExceededTheSupportedSize
        => Get(nameof(GitProcessGitOutputExceededTheSupportedSize));

    /// <summary>
    /// Gets the localized text: The private Git operation timed out.
    /// </summary>
    public static string GitProcessThePrivateGitOperationTimedOut
        => Get(nameof(GitProcessThePrivateGitOperationTimedOut));

    /// <summary>
    /// Gets the localized text: The private Git process could not start.
    /// </summary>
    public static string GitProcessThePrivateGitProcessCouldNotStart
        => Get(nameof(GitProcessThePrivateGitProcessCouldNotStart));

    /// <summary>
    /// Gets the localized text: The game inventory does not match its release checksum.
    /// </summary>
    public static string HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseChecksum
        => Get(nameof(HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseChecksum));

    /// <summary>
    /// Gets the localized text: The game inventory does not match its release identity.
    /// </summary>
    public static string HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseIdentity
        => Get(nameof(HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseIdentity));

    /// <summary>
    /// Gets the localized text: The release game inventory is empty.
    /// </summary>
    public static string HoennGameBaselineTheReleaseGameInventoryIsEmpty
        => Get(nameof(HoennGameBaselineTheReleaseGameInventoryIsEmpty));

    /// <summary>
    /// Gets the localized text: The release game inventory is missing.
    /// </summary>
    public static string HoennGameBaselineTheReleaseGameInventoryIsMissing
        => Get(nameof(HoennGameBaselineTheReleaseGameInventoryIsMissing));

    /// <summary>
    /// Gets the localized text: The release game inventory manifest is empty.
    /// </summary>
    public static string HoennGameBaselineTheReleaseGameInventoryManifestIsEmpty
        => Get(nameof(HoennGameBaselineTheReleaseGameInventoryManifestIsEmpty));

    /// <summary>
    /// Gets the localized text: The release game inventory manifest is missing.
    /// </summary>
    public static string HoennGameBaselineTheReleaseGameInventoryManifestIsMissing
        => Get(nameof(HoennGameBaselineTheReleaseGameInventoryManifestIsMissing));

    /// <summary>
    /// Gets the localized text: Missing download redirect.
    /// </summary>
    public static string IArtifactSourceMissingDownloadRedirect
        => Get(nameof(IArtifactSourceMissingDownloadRedirect));

    /// <summary>
    /// Gets the localized text: The artifact exceeds its declared size.
    /// </summary>
    public static string IArtifactSourceTheArtifactExceedsItsDeclaredSize
        => Get(nameof(IArtifactSourceTheArtifactExceedsItsDeclaredSize));

    /// <summary>
    /// Gets the localized text: The artifact source is not an allowed public HTTPS host.
    /// </summary>
    public static string IArtifactSourceTheArtifactSourceIsNotAnAllowedPublicHTTPS
        => Get(nameof(IArtifactSourceTheArtifactSourceIsNotAnAllowedPublicHTTPS));

    /// <summary>
    /// Gets the localized text: Too many download redirects.
    /// </summary>
    public static string IArtifactSourceTooManyDownloadRedirects
        => Get(nameof(IArtifactSourceTooManyDownloadRedirects));

    /// <summary>
    /// Gets the localized text: Git and updater metadata must be ordinary directories.
    /// </summary>
    public static string InstallationFileSnapshotGitAndUpdaterMetadataMustBeOrdinaryDirectories
        => Get(nameof(InstallationFileSnapshotGitAndUpdaterMetadataMustBeOrdinaryDirectories));

    /// <summary>
    /// Gets the localized text: The installation has too many entries for automatic file planning.
    /// </summary>
    public static string InstallationFileSnapshotTheInstallationHasTooManyEntriesForAutomaticFile
        => Get(nameof(InstallationFileSnapshotTheInstallationHasTooManyEntriesForAutomaticFile));

    /// <summary>
    /// Gets the localized text: The installation directory identity could not be read.
    /// </summary>
    public static string InstallationIdentityTheInstallationDirectoryIdentityCouldNotBeRead
        => Get(nameof(InstallationIdentityTheInstallationDirectoryIdentityCouldNotBeRead));

    /// <summary>
    /// Gets the localized text: Updater ancestors must be ordinary directories.
    /// </summary>
    public static string InstallationIdentityUpdaterAncestorsMustBeOrdinaryDirectories
        => Get(nameof(InstallationIdentityUpdaterAncestorsMustBeOrdinaryDirectories));

    /// <summary>
    /// Gets the localized text: Updater input must be an ordinary file with one filesystem link.
    /// </summary>
    public static string InstallationIdentityUpdaterInputMustBeAnOrdinaryFileWithOne
        => Get(nameof(InstallationIdentityUpdaterInputMustBeAnOrdinaryFileWithOne));

    /// <summary>
    /// Gets the localized text: An unfinished update must be recovered before another installation writer can start.
    /// </summary>
    public static string InstallationLeaseAnUnfinishedUpdateMustBeRecoveredBeforeAnotherInstallation
        => Get(nameof(InstallationLeaseAnUnfinishedUpdateMustBeRecoveredBeforeAnotherInstallation));

    /// <summary>
    /// Gets the localized text: The installation directory no longer exists.
    /// </summary>
    public static string InstallationLeaseTheInstallationDirectoryNoLongerExists
        => Get(nameof(InstallationLeaseTheInstallationDirectoryNoLongerExists));

    /// <summary>
    /// Gets the localized text: Installation work is already running or paused for an update.
    /// </summary>
    public static string InstallationWriterCoordinatorInstallationWorkIsAlreadyRunningOrPausedForAn
        => Get(nameof(InstallationWriterCoordinatorInstallationWorkIsAlreadyRunningOrPausedForAn));

    /// <summary>
    /// Gets the localized text: A file changed after you reviewed its replacement. Check the update again and review the new file conflicts.
    /// </summary>
    public static string IronmonOnlyUpdateAFileChangedAfterYouReviewedItsReplacementCheck
        => Get(nameof(IronmonOnlyUpdateAFileChangedAfterYouReviewedItsReplacementCheck));

    /// <summary>
    /// Gets the localized text: Finish the active run before installing this release.
    /// </summary>
    public static string IronmonOnlyUpdateFinishTheActiveRunBeforeInstallingThisRelease
        => Get(nameof(IronmonOnlyUpdateFinishTheActiveRunBeforeInstallingThisRelease));

    /// <summary>
    /// Gets the localized text: Some installed files need an explicit replacement decision before this update can continue.
    /// </summary>
    public static string IronmonOnlyUpdateSomeInstalledFilesNeedAnExplicitReplacementDecisionBefore
        => Get(nameof(IronmonOnlyUpdateSomeInstalledFilesNeedAnExplicitReplacementDecisionBefore));

    /// <summary>
    /// Gets the localized text: The complete signed recovery evidence exceeds the supported metadata budget.
    /// </summary>
    public static string IronmonOnlyUpdateTheCompleteSignedRecoveryEvidenceExceedsTheSupportedMetadata
        => Get(nameof(IronmonOnlyUpdateTheCompleteSignedRecoveryEvidenceExceedsTheSupportedMetadata));

    /// <summary>
    /// Gets the localized text: The current signed release does not match the selected upgrade baseline.
    /// </summary>
    public static string IronmonOnlyUpdateTheCurrentSignedReleaseDoesNotMatchTheSelected
        => Get(nameof(IronmonOnlyUpdateTheCurrentSignedReleaseDoesNotMatchTheSelected));

    /// <summary>
    /// Gets the localized text: The installation purpose does not match the selected baseline.
    /// </summary>
    public static string IronmonOnlyUpdateTheInstallationPurposeDoesNotMatchTheSelectedBaseline
        => Get(nameof(IronmonOnlyUpdateTheInstallationPurposeDoesNotMatchTheSelectedBaseline));

    /// <summary>
    /// Gets the localized text: The required program file &apos;{0}&apos; was changed or removed locally. Restore it from the installed release before updating.
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string IronmonOnlyUpdateTheRequiredProgramFileWasChangedOrRemovedLocally(object? value0)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(IronmonOnlyUpdateTheRequiredProgramFileWasChangedOrRemovedLocally)), value0);

    /// <summary>
    /// Gets the localized text: The selected update is not a supported forward version or package flavor.
    /// </summary>
    public static string IronmonOnlyUpdateTheSelectedUpdateIsNotASupportedForwardVersion
        => Get(nameof(IronmonOnlyUpdateTheSelectedUpdateIsNotASupportedForwardVersion));

    /// <summary>
    /// Gets the localized text: The signed inventory exceeds the supported metadata budget.
    /// </summary>
    public static string IronmonOnlyUpdateTheSignedInventoryExceedsTheSupportedMetadataBudget
        => Get(nameof(IronmonOnlyUpdateTheSignedInventoryExceedsTheSupportedMetadataBudget));

    /// <summary>
    /// Gets the localized text: This release needs a combined game update; the Ironmon-only path cannot change the game.
    /// </summary>
    public static string IronmonOnlyUpdateThisReleaseNeedsACombinedGameUpdateTheIronmon
        => Get(nameof(IronmonOnlyUpdateThisReleaseNeedsACombinedGameUpdateTheIronmon));

    /// <summary>
    /// Gets the localized text: This release requires a newer verified updater. Use the release&apos;s installer before updating this installation.
    /// </summary>
    public static string IronmonOnlyUpdateThisReleaseRequiresANewerVerifiedUpdaterUseThe
        => Get(nameof(IronmonOnlyUpdateThisReleaseRequiresANewerVerifiedUpdaterUseThe));

    /// <summary>
    /// Gets the localized text: Update downloads must remain outside the installed game and tracker.
    /// </summary>
    public static string IronmonOnlyUpdateUpdateDownloadsMustRemainOutsideTheInstalledGameAnd
        => Get(nameof(IronmonOnlyUpdateUpdateDownloadsMustRemainOutsideTheInstalledGameAnd));

    /// <summary>
    /// Gets the localized text: The archive contains a duplicate or unsupported entry.
    /// </summary>
    public static string MinGitCacheTheArchiveContainsADuplicateOrUnsupportedEntry
        => Get(nameof(MinGitCacheTheArchiveContainsADuplicateOrUnsupportedEntry));

    /// <summary>
    /// Gets the localized text: The archive has too many entries.
    /// </summary>
    public static string MinGitCacheTheArchiveHasTooManyEntries
        => Get(nameof(MinGitCacheTheArchiveHasTooManyEntries));

    /// <summary>
    /// Gets the localized text: The expanded Git archive is too large.
    /// </summary>
    public static string MinGitCacheTheExpandedGitArchiveIsTooLarge
        => Get(nameof(MinGitCacheTheExpandedGitArchiveIsTooLarge));

    /// <summary>
    /// Gets the localized text: The extracted Git runtime failed verification.
    /// </summary>
    public static string MinGitCacheTheExtractedGitRuntimeFailedVerification
        => Get(nameof(MinGitCacheTheExtractedGitRuntimeFailedVerification));

    /// <summary>
    /// Gets the localized text: The Git archive is missing its executable or license.
    /// </summary>
    public static string MinGitCacheTheGitArchiveIsMissingItsExecutableOrLicense
        => Get(nameof(MinGitCacheTheGitArchiveIsMissingItsExecutableOrLicense));

    /// <summary>
    /// Gets the localized text: The MinGit archive failed size or SHA-256 verification.
    /// </summary>
    public static string MinGitCacheTheMinGitArchiveFailedSizeOrSHA256Verification
        => Get(nameof(MinGitCacheTheMinGitArchiveFailedSizeOrSHA256Verification));

    /// <summary>
    /// Gets the localized text: Redirected files are not supported.
    /// </summary>
    public static string PlainPathsRedirectedFilesAreNotSupported
        => Get(nameof(PlainPathsRedirectedFilesAreNotSupported));

    /// <summary>
    /// Gets the localized text: Redirected installation or cache paths are not supported.
    /// </summary>
    public static string PlainPathsRedirectedInstallationOrCachePathsAreNotSupported
        => Get(nameof(PlainPathsRedirectedInstallationOrCachePathsAreNotSupported));

    /// <summary>
    /// Gets the localized text: Refusing to remove a directory outside the owned parent.
    /// </summary>
    public static string PlainPathsRefusingToRemoveADirectoryOutsideTheOwnedParent
        => Get(nameof(PlainPathsRefusingToRemoveADirectoryOutsideTheOwnedParent));

    /// <summary>
    /// Gets the localized text: The archive contains an unsafe path.
    /// </summary>
    public static string PlainPathsTheArchiveContainsAnUnsafePath
        => Get(nameof(PlainPathsTheArchiveContainsAnUnsafePath));

    /// <summary>
    /// Gets the localized text: The archive path escapes its destination.
    /// </summary>
    public static string PlainPathsTheArchivePathEscapesItsDestination
        => Get(nameof(PlainPathsTheArchivePathEscapesItsDestination));

    /// <summary>
    /// Gets the localized text: Updater paths must be fully qualified local paths.
    /// </summary>
    public static string PlainPathsUpdaterPathsMustBeFullyQualifiedLocalPaths
        => Get(nameof(PlainPathsUpdaterPathsMustBeFullyQualifiedLocalPaths));

    /// <summary>
    /// Gets the localized text: An exact lowercase SHA-1 commit is required.
    /// </summary>
    public static string PrivateGitProviderAnExactLowercaseSHA1CommitIsRequired
        => Get(nameof(PrivateGitProviderAnExactLowercaseSHA1CommitIsRequired));

    /// <summary>
    /// Gets the localized text: Git could not verify or prepare the requested repository state.
    /// </summary>
    public static string PrivateGitProviderGitCouldNotVerifyOrPrepareTheRequestedRepository
        => Get(nameof(PrivateGitProviderGitCouldNotVerifyOrPrepareTheRequestedRepository));

    /// <summary>
    /// Gets the localized text: Git did not identify the expected installation root.
    /// </summary>
    public static string PrivateGitProviderGitDidNotIdentifyTheExpectedInstallationRoot
        => Get(nameof(PrivateGitProviderGitDidNotIdentifyTheExpectedInstallationRoot));

    /// <summary>
    /// Gets the localized text: Hidden index changes are not supported.
    /// </summary>
    public static string PrivateGitProviderHiddenIndexChangesAreNotSupported
        => Get(nameof(PrivateGitProviderHiddenIndexChangesAreNotSupported));

    /// <summary>
    /// Gets the localized text: Object staging and the installation must be separate directories.
    /// </summary>
    public static string PrivateGitProviderObjectStagingAndTheInstallationMustBeSeparateDirectories
        => Get(nameof(PrivateGitProviderObjectStagingAndTheInstallationMustBeSeparateDirectories));

    /// <summary>
    /// Gets the localized text: Only ordinary Git checkouts are supported in this step.
    /// </summary>
    public static string PrivateGitProviderOnlyOrdinaryGitCheckoutsAreSupportedInThisStep
        => Get(nameof(PrivateGitProviderOnlyOrdinaryGitCheckoutsAreSupportedInThisStep));

    /// <summary>
    /// Gets the localized text: Repositories with submodules are not supported.
    /// </summary>
    public static string PrivateGitProviderRepositoriesWithSubmodulesAreNotSupported
        => Get(nameof(PrivateGitProviderRepositoriesWithSubmodulesAreNotSupported));

    /// <summary>
    /// Gets the localized text: Submodules in an approved revision require a separate policy.
    /// </summary>
    public static string PrivateGitProviderSubmodulesInAnApprovedRevisionRequireASeparatePolicy
        => Get(nameof(PrivateGitProviderSubmodulesInAnApprovedRevisionRequireASeparatePolicy));

    /// <summary>
    /// Gets the localized text: The approved object is not a commit.
    /// </summary>
    public static string PrivateGitProviderTheApprovedObjectIsNotACommit
        => Get(nameof(PrivateGitProviderTheApprovedObjectIsNotACommit));

    /// <summary>
    /// Gets the localized text: The index contains unsupported modes or submodules.
    /// </summary>
    public static string PrivateGitProviderTheIndexContainsUnsupportedModesOrSubmodules
        => Get(nameof(PrivateGitProviderTheIndexContainsUnsupportedModesOrSubmodules));

    /// <summary>
    /// Gets the localized text: The installation changed during preparation.
    /// </summary>
    public static string PrivateGitProviderTheInstallationChangedDuringPreparation
        => Get(nameof(PrivateGitProviderTheInstallationChangedDuringPreparation));

    /// <summary>
    /// Gets the localized text: The installation does not exist.
    /// </summary>
    public static string PrivateGitProviderTheInstallationDoesNotExist
        => Get(nameof(PrivateGitProviderTheInstallationDoesNotExist));

    /// <summary>
    /// Gets the localized text: The installation is not on its expected release branch.
    /// </summary>
    public static string PrivateGitProviderTheInstallationIsNotOnItsExpectedReleaseBranch
        => Get(nameof(PrivateGitProviderTheInstallationIsNotOnItsExpectedReleaseBranch));

    /// <summary>
    /// Gets the localized text: The repository contains custom hooks.
    /// </summary>
    public static string PrivateGitProviderTheRepositoryContainsCustomHooks
        => Get(nameof(PrivateGitProviderTheRepositoryContainsCustomHooks));

    /// <summary>
    /// Gets the localized text: This repository layout requires an unsupported metadata policy.
    /// </summary>
    public static string PrivateGitProviderThisRepositoryLayoutRequiresAnUnsupportedMetadataPolicy
        => Get(nameof(PrivateGitProviderThisRepositoryLayoutRequiresAnUnsupportedMetadataPolicy));

    /// <summary>
    /// Gets the localized text: Administrator installation is supported on Windows only.
    /// </summary>
    public static string ProtectedUpdateClientAdministratorInstallationIsSupportedOnWindowsOnly
        => Get(nameof(ProtectedUpdateClientAdministratorInstallationIsSupportedOnWindowsOnly));

    /// <summary>
    /// Gets the localized text: Administrator work was cancelled safely. Recovery files were retained when needed.
    /// </summary>
    public static string ProtectedUpdateClientAdministratorWorkWasCancelledSafelyRecoveryFilesWereRetained
        => Get(nameof(ProtectedUpdateClientAdministratorWorkWasCancelledSafelyRecoveryFilesWereRetained));

    /// <summary>
    /// Gets the localized text: An unexpected process owns the administrator endpoint.
    /// </summary>
    public static string ProtectedUpdateClientAnUnexpectedProcessOwnsTheAdministratorEndpoint
        => Get(nameof(ProtectedUpdateClientAnUnexpectedProcessOwnsTheAdministratorEndpoint));

    /// <summary>
    /// Gets the localized text: No administrator session is attached to this installation.
    /// </summary>
    public static string ProtectedUpdateClientNoAdministratorSessionIsAttachedToThisInstallation
        => Get(nameof(ProtectedUpdateClientNoAdministratorSessionIsAttachedToThisInstallation));

    /// <summary>
    /// Gets the localized text: No administrator session is available.
    /// </summary>
    public static string ProtectedUpdateClientNoAdministratorSessionIsAvailable
        => Get(nameof(ProtectedUpdateClientNoAdministratorSessionIsAvailable));

    /// <summary>
    /// Gets the localized text: The administrator helper changed before launch.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperChangedBeforeLaunch
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperChangedBeforeLaunch));

    /// <summary>
    /// Gets the localized text: The administrator helper did not become available. Retry the operation; if an update was interrupted, use recovery before starting the game.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperDidNotBecomeAvailableRetryThe
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperDidNotBecomeAvailableRetryThe));

    /// <summary>
    /// Gets the localized text: The administrator helper did not return a prepared installation.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperDidNotReturnAPreparedInstallation
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperDidNotReturnAPreparedInstallation));

    /// <summary>
    /// Gets the localized text: The administrator helper has not confirmed completion. Keep the game closed and use recovery if the operation was interrupted.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperHasNotConfirmedCompletionKeepThe
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperHasNotConfirmedCompletionKeepThe));

    /// <summary>
    /// Gets the localized text: The administrator helper omitted the installation result.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperOmittedTheInstallationResult
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperOmittedTheInstallationResult));

    /// <summary>
    /// Gets the localized text: The administrator helper omitted the recovery result.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperOmittedTheRecoveryResult
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperOmittedTheRecoveryResult));

    /// <summary>
    /// Gets the localized text: The administrator helper omitted the sprite result.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorHelperOmittedTheSpriteResult
        => Get(nameof(ProtectedUpdateClientTheAdministratorHelperOmittedTheSpriteResult));

    /// <summary>
    /// Gets the localized text: The administrator result does not match the reviewed release.
    /// </summary>
    public static string ProtectedUpdateClientTheAdministratorResultDoesNotMatchTheReviewedRelease
        => Get(nameof(ProtectedUpdateClientTheAdministratorResultDoesNotMatchTheReviewedRelease));

    /// <summary>
    /// Gets the localized text: The installation parent no longer exists.
    /// </summary>
    public static string ProtectedUpdateClientTheInstallationParentNoLongerExists
        => Get(nameof(ProtectedUpdateClientTheInstallationParentNoLongerExists));

    /// <summary>
    /// Gets the localized text: The installed release record belongs to another folder.
    /// </summary>
    public static string ProtectedUpdateClientTheInstalledReleaseRecordBelongsToAnotherFolder
        => Get(nameof(ProtectedUpdateClientTheInstalledReleaseRecordBelongsToAnotherFolder));

    /// <summary>
    /// Gets the localized text: The installed release verification records are missing. Repair this folder with Ironmon Setup before downloading sprites with administrator access.
    /// </summary>
    public static string ProtectedUpdateClientTheInstalledReleaseVerificationRecordsAreMissingRepairThis
        => Get(nameof(ProtectedUpdateClientTheInstalledReleaseVerificationRecordsAreMissingRepairThis));

    /// <summary>
    /// Gets the localized text: The recovery evidence belongs to another installation.
    /// </summary>
    public static string ProtectedUpdateClientTheRecoveryEvidenceBelongsToAnotherInstallation
        => Get(nameof(ProtectedUpdateClientTheRecoveryEvidenceBelongsToAnotherInstallation));

    /// <summary>
    /// Gets the localized text: The administrator message exceeds its size limit.
    /// </summary>
    public static string ProtectedUpdateProtocolTheAdministratorMessageExceedsItsSizeLimit
        => Get(nameof(ProtectedUpdateProtocolTheAdministratorMessageExceedsItsSizeLimit));

    /// <summary>
    /// Gets the localized text: The administrator message length is invalid.
    /// </summary>
    public static string ProtectedUpdateProtocolTheAdministratorMessageLengthIsInvalid
        => Get(nameof(ProtectedUpdateProtocolTheAdministratorMessageLengthIsInvalid));

    /// <summary>
    /// Gets the localized text: The administrator pipe client could not be verified.
    /// </summary>
    public static string ProtectedUpdateProtocolTheAdministratorPipeClientCouldNotBeVerified
        => Get(nameof(ProtectedUpdateProtocolTheAdministratorPipeClientCouldNotBeVerified));

    /// <summary>
    /// Gets the localized text: The administrator pipe owner could not be verified.
    /// </summary>
    public static string ProtectedUpdateProtocolTheAdministratorPipeOwnerCouldNotBeVerified
        => Get(nameof(ProtectedUpdateProtocolTheAdministratorPipeOwnerCouldNotBeVerified));

    /// <summary>
    /// Gets the localized text: The administrator session identity is invalid.
    /// </summary>
    public static string ProtectedUpdateProtocolTheAdministratorSessionIdentityIsInvalid
        => Get(nameof(ProtectedUpdateProtocolTheAdministratorSessionIdentityIsInvalid));

    /// <summary>
    /// Gets the localized text: An unexpected process connected to the administrator session.
    /// </summary>
    public static string ProtectedUpdateServerAnUnexpectedProcessConnectedToTheAdministratorSession
        => Get(nameof(ProtectedUpdateServerAnUnexpectedProcessConnectedToTheAdministratorSession));

    /// <summary>
    /// Gets the localized text: The administrator capability does not match this session.
    /// </summary>
    public static string ProtectedUpdateServerTheAdministratorCapabilityDoesNotMatchThisSession
        => Get(nameof(ProtectedUpdateServerTheAdministratorCapabilityDoesNotMatchThisSession));

    /// <summary>
    /// Gets the localized text: The helper executable could not be located.
    /// </summary>
    public static string ProtectedUpdateServerTheHelperExecutableCouldNotBeLocated
        => Get(nameof(ProtectedUpdateServerTheHelperExecutableCouldNotBeLocated));

    /// <summary>
    /// Gets the localized text: Windows did not grant administrator permission.
    /// </summary>
    public static string ProtectedUpdateServerWindowsDidNotGrantAdministratorPermission
        => Get(nameof(ProtectedUpdateServerWindowsDidNotGrantAdministratorPermission));

    /// <summary>
    /// Gets the localized text: An administrator session accepts only one installation selection.
    /// </summary>
    public static string ProtectedUpdateWorkerAnAdministratorSessionAcceptsOnlyOneInstallationSelection
        => Get(nameof(ProtectedUpdateWorkerAnAdministratorSessionAcceptsOnlyOneInstallationSelection));

    /// <summary>
    /// Gets the localized text: A new preparation cannot select an existing transaction.
    /// </summary>
    public static string ProtectedUpdateWorkerANewPreparationCannotSelectAnExistingTransaction
        => Get(nameof(ProtectedUpdateWorkerANewPreparationCannotSelectAnExistingTransaction));

    /// <summary>
    /// Gets the localized text: A recovery session cannot apply a new installation.
    /// </summary>
    public static string ProtectedUpdateWorkerARecoverySessionCannotApplyANewInstallation
        => Get(nameof(ProtectedUpdateWorkerARecoverySessionCannotApplyANewInstallation));

    /// <summary>
    /// Gets the localized text: Finish recovery before downloading optional sprites.
    /// </summary>
    public static string ProtectedUpdateWorkerFinishRecoveryBeforeDownloadingOptionalSprites
        => Get(nameof(ProtectedUpdateWorkerFinishRecoveryBeforeDownloadingOptionalSprites));

    /// <summary>
    /// Gets the localized text: Finish the active run before changing the game.
    /// </summary>
    public static string ProtectedUpdateWorkerFinishTheActiveRunBeforeChangingTheGame
        => Get(nameof(ProtectedUpdateWorkerFinishTheActiveRunBeforeChangingTheGame));

    /// <summary>
    /// Gets the localized text: Optional sprite work requires a committed core installation.
    /// </summary>
    public static string ProtectedUpdateWorkerOptionalSpriteWorkRequiresACommittedCoreInstallation
        => Get(nameof(ProtectedUpdateWorkerOptionalSpriteWorkRequiresACommittedCoreInstallation));

    /// <summary>
    /// Gets the localized text: Repair the installed Ironmon package before downloading sprites with administrator access.
    /// </summary>
    public static string ProtectedUpdateWorkerRepairTheInstalledIronmonPackageBeforeDownloadingSpritesWith
        => Get(nameof(ProtectedUpdateWorkerRepairTheInstalledIronmonPackageBeforeDownloadingSpritesWith));

    /// <summary>
    /// Gets the localized text: Signed installation evidence is required.
    /// </summary>
    public static string ProtectedUpdateWorkerSignedInstallationEvidenceIsRequired
        => Get(nameof(ProtectedUpdateWorkerSignedInstallationEvidenceIsRequired));

    /// <summary>
    /// Gets the localized text: The administrator installation has not been prepared.
    /// </summary>
    public static string ProtectedUpdateWorkerTheAdministratorInstallationHasNotBeenPrepared
        => Get(nameof(ProtectedUpdateWorkerTheAdministratorInstallationHasNotBeenPrepared));

    /// <summary>
    /// Gets the localized text: The administrator installation root differs from the reviewed selection.
    /// </summary>
    public static string ProtectedUpdateWorkerTheAdministratorInstallationRootDiffersFromTheReviewedSelection
        => Get(nameof(ProtectedUpdateWorkerTheAdministratorInstallationRootDiffersFromTheReviewedSelection));

    /// <summary>
    /// Gets the localized text: The administrator request differs from the reviewed transaction.
    /// </summary>
    public static string ProtectedUpdateWorkerTheAdministratorRequestDiffersFromTheReviewedTransaction
        => Get(nameof(ProtectedUpdateWorkerTheAdministratorRequestDiffersFromTheReviewedTransaction));

    /// <summary>
    /// Gets the localized text: The elevated executable does not match the authenticated helper package.
    /// </summary>
    public static string ProtectedUpdateWorkerTheElevatedExecutableDoesNotMatchTheAuthenticatedHelper
        => Get(nameof(ProtectedUpdateWorkerTheElevatedExecutableDoesNotMatchTheAuthenticatedHelper));

    /// <summary>
    /// Gets the localized text: The installation directory changed while Windows requested permission. Review the folder again.
    /// </summary>
    public static string ProtectedUpdateWorkerTheInstallationDirectoryChangedWhileWindowsRequestedPermissionReview
        => Get(nameof(ProtectedUpdateWorkerTheInstallationDirectoryChangedWhileWindowsRequestedPermissionReview));

    /// <summary>
    /// Gets the localized text: The installed release changed before administrator access was granted.
    /// </summary>
    public static string ProtectedUpdateWorkerTheInstalledReleaseChangedBeforeAdministratorAccessWasGranted
        => Get(nameof(ProtectedUpdateWorkerTheInstalledReleaseChangedBeforeAdministratorAccessWasGranted));

    /// <summary>
    /// Gets the localized text: The recovery authorization changed while Windows requested permission.
    /// </summary>
    public static string ProtectedUpdateWorkerTheRecoveryAuthorizationChangedWhileWindowsRequestedPermission
        => Get(nameof(ProtectedUpdateWorkerTheRecoveryAuthorizationChangedWhileWindowsRequestedPermission));

    /// <summary>
    /// Gets the localized text: The recovery transaction is missing.
    /// </summary>
    public static string ProtectedUpdateWorkerTheRecoveryTransactionIsMissing
        => Get(nameof(ProtectedUpdateWorkerTheRecoveryTransactionIsMissing));

    /// <summary>
    /// Gets the localized text: The reviewed installation parent is missing.
    /// </summary>
    public static string ProtectedUpdateWorkerTheReviewedInstallationParentIsMissing
        => Get(nameof(ProtectedUpdateWorkerTheReviewedInstallationParentIsMissing));

    /// <summary>
    /// Gets the localized text: The running helper is unavailable.
    /// </summary>
    public static string ProtectedUpdateWorkerTheRunningHelperIsUnavailable
        => Get(nameof(ProtectedUpdateWorkerTheRunningHelperIsUnavailable));

    /// <summary>
    /// Gets the localized text: The tracker navigation context is missing.
    /// </summary>
    public static string ProtectedUpdateWorkerTheTrackerNavigationContextIsMissing
        => Get(nameof(ProtectedUpdateWorkerTheTrackerNavigationContextIsMissing));

    /// <summary>
    /// Gets the localized text: This administrator operation is not supported.
    /// </summary>
    public static string ProtectedUpdateWorkerThisAdministratorOperationIsNotSupported
        => Get(nameof(ProtectedUpdateWorkerThisAdministratorOperationIsNotSupported));

    /// <summary>
    /// Gets the localized text: This administrator session is restricted to optional sprite work.
    /// </summary>
    public static string ProtectedUpdateWorkerThisAdministratorSessionIsRestrictedToOptionalSpriteWork
        => Get(nameof(ProtectedUpdateWorkerThisAdministratorSessionIsRestrictedToOptionalSpriteWork));

    /// <summary>
    /// Gets the localized text: An existing immutable updater version has different content.
    /// </summary>
    public static string RecoveryHelperPackageAnExistingImmutableUpdaterVersionHasDifferentContent
        => Get(nameof(RecoveryHelperPackageAnExistingImmutableUpdaterVersionHasDifferentContent));

    /// <summary>
    /// Gets the localized text: The authenticated helper package must contain one bounded updater executable.
    /// </summary>
    public static string RecoveryHelperPackageTheAuthenticatedHelperPackageMustContainOneBoundedUpdater
        => Get(nameof(RecoveryHelperPackageTheAuthenticatedHelperPackageMustContainOneBoundedUpdater));

    /// <summary>
    /// Gets the localized text: The embedded helper cannot be an archive link.
    /// </summary>
    public static string RecoveryHelperPackageTheEmbeddedHelperCannotBeAnArchiveLink
        => Get(nameof(RecoveryHelperPackageTheEmbeddedHelperCannotBeAnArchiveLink));

    /// <summary>
    /// Gets the localized text: The embedded helper differs from its signed executable identity.
    /// </summary>
    public static string RecoveryHelperPackageTheEmbeddedHelperDiffersFromItsSignedExecutableIdentity
        => Get(nameof(RecoveryHelperPackageTheEmbeddedHelperDiffersFromItsSignedExecutableIdentity));

    /// <summary>
    /// Gets the localized text: The embedded helper exceeds its signed size.
    /// </summary>
    public static string RecoveryHelperPackageTheEmbeddedHelperExceedsItsSignedSize
        => Get(nameof(RecoveryHelperPackageTheEmbeddedHelperExceedsItsSignedSize));

    /// <summary>
    /// Gets the localized text: The embedded helper identity is missing.
    /// </summary>
    public static string RecoveryHelperPackageTheEmbeddedHelperIdentityIsMissing
        => Get(nameof(RecoveryHelperPackageTheEmbeddedHelperIdentityIsMissing));

    /// <summary>
    /// Gets the localized text: The helper exceeds its declared expanded size.
    /// </summary>
    public static string RecoveryHelperPackageTheHelperExceedsItsDeclaredExpandedSize
        => Get(nameof(RecoveryHelperPackageTheHelperExceedsItsDeclaredExpandedSize));

    /// <summary>
    /// Gets the localized text: The helper package is incomplete.
    /// </summary>
    public static string RecoveryHelperPackageTheHelperPackageIsIncomplete
        => Get(nameof(RecoveryHelperPackageTheHelperPackageIsIncomplete));

    /// <summary>
    /// Gets the localized text: The package inventory disagrees with the signed embedded helper.
    /// </summary>
    public static string RecoveryHelperPackageThePackageInventoryDisagreesWithTheSignedEmbeddedHelper
        => Get(nameof(RecoveryHelperPackageThePackageInventoryDisagreesWithTheSignedEmbeddedHelper));

    /// <summary>
    /// Gets the localized text: The player package has no unique bounded embedded helper.
    /// </summary>
    public static string RecoveryHelperPackageThePlayerPackageHasNoUniqueBoundedEmbeddedHelper
        => Get(nameof(RecoveryHelperPackageThePlayerPackageHasNoUniqueBoundedEmbeddedHelper));

    /// <summary>
    /// Gets the localized text: An extracted file failed its signed content verification.
    /// </summary>
    public static string ReleaseArchiveAnExtractedFileFailedItsSignedContentVerification
        => Get(nameof(ReleaseArchiveAnExtractedFileFailedItsSignedContentVerification));

    /// <summary>
    /// Gets the localized text: An update archive must be extracted into a new private directory.
    /// </summary>
    public static string ReleaseArchiveAnUpdateArchiveMustBeExtractedIntoANew
        => Get(nameof(ReleaseArchiveAnUpdateArchiveMustBeExtractedIntoANew));

    /// <summary>
    /// Gets the localized text: The downloaded archive does not match its signed fingerprint.
    /// </summary>
    public static string ReleaseArchiveTheDownloadedArchiveDoesNotMatchItsSignedFingerprint
        => Get(nameof(ReleaseArchiveTheDownloadedArchiveDoesNotMatchItsSignedFingerprint));

    /// <summary>
    /// Gets the localized text: The expanded package exceeds its signed file size.
    /// </summary>
    public static string ReleaseArchiveTheExpandedPackageExceedsItsSignedFileSize
        => Get(nameof(ReleaseArchiveTheExpandedPackageExceedsItsSignedFileSize));

    /// <summary>
    /// Gets the localized text: The package archive contains too many entries.
    /// </summary>
    public static string ReleaseArchiveThePackageArchiveContainsTooManyEntries
        => Get(nameof(ReleaseArchiveThePackageArchiveContainsTooManyEntries));

    /// <summary>
    /// Gets the localized text: The package contains an unlisted file or an unexpected file size.
    /// </summary>
    public static string ReleaseArchiveThePackageContainsAnUnlistedFileOrAnUnexpected
        => Get(nameof(ReleaseArchiveThePackageContainsAnUnlistedFileOrAnUnexpected));

    /// <summary>
    /// Gets the localized text: The package contains an unlisted or incorrectly cased directory.
    /// </summary>
    public static string ReleaseArchiveThePackageContainsAnUnlistedOrIncorrectlyCasedDirectory
        => Get(nameof(ReleaseArchiveThePackageContainsAnUnlistedOrIncorrectlyCasedDirectory));

    /// <summary>
    /// Gets the localized text: The package contains duplicate paths, links or unsupported file types.
    /// </summary>
    public static string ReleaseArchiveThePackageContainsDuplicatePathsLinksOrUnsupportedFile
        => Get(nameof(ReleaseArchiveThePackageContainsDuplicatePathsLinksOrUnsupportedFile));

    /// <summary>
    /// Gets the localized text: The package is missing signed inventory files.
    /// </summary>
    public static string ReleaseArchiveThePackageIsMissingSignedInventoryFiles
        => Get(nameof(ReleaseArchiveThePackageIsMissingSignedInventoryFiles));

    /// <summary>
    /// Gets the localized text: A conditional release response has no authenticated cached release.
    /// </summary>
    public static string ReleaseDiscoveryAConditionalReleaseResponseHasNoAuthenticatedCachedRelease
        => Get(nameof(ReleaseDiscoveryAConditionalReleaseResponseHasNoAuthenticatedCachedRelease));

    /// <summary>
    /// Gets the localized text: No public Ironmon release is available right now. Try checking again later.
    /// </summary>
    public static string ReleaseDiscoveryNoPubliclyAccessibleIronmonReleaseWasFoundCheckThat
        => Get(nameof(ReleaseDiscoveryNoPubliclyAccessibleIronmonReleaseWasFoundCheckThat));

    /// <summary>
    /// Gets the localized text: Only published stable releases from the fixed repository are eligible.
    /// </summary>
    public static string ReleaseDiscoveryOnlyPublishedStableReleasesFromTheFixedRepositoryAre
        => Get(nameof(ReleaseDiscoveryOnlyPublishedStableReleasesFromTheFixedRepositoryAre));

    /// <summary>
    /// Gets the localized text: Release checking is temporarily rate limited.
    /// </summary>
    public static string ReleaseDiscoveryReleaseCheckingIsTemporarilyRateLimited
        => Get(nameof(ReleaseDiscoveryReleaseCheckingIsTemporarilyRateLimited));

    /// <summary>
    /// Gets the localized text: The public release asset names are ambiguous.
    /// </summary>
    public static string ReleaseDiscoveryThePublicReleaseAssetNamesAreAmbiguous
        => Get(nameof(ReleaseDiscoveryThePublicReleaseAssetNamesAreAmbiguous));

    /// <summary>
    /// Gets the localized text: The release cache watermark does not match its authenticated observation.
    /// </summary>
    public static string ReleaseDiscoveryTheReleaseCacheWatermarkDoesNotMatchItsAuthenticated
        => Get(nameof(ReleaseDiscoveryTheReleaseCacheWatermarkDoesNotMatchItsAuthenticated));

    /// <summary>
    /// Gets the localized text: The release feed attempted to roll back or replace a previously authenticated release sequence.
    /// </summary>
    public static string ReleaseDiscoveryTheReleaseFeedAttemptedToRollBackOrReplace
        => Get(nameof(ReleaseDiscoveryTheReleaseFeedAttemptedToRollBackOrReplace));

    /// <summary>
    /// Gets the localized text: The release is missing its repository-bound manifest or signature.
    /// </summary>
    public static string ReleaseDiscoveryTheReleaseIsMissingItsRepositoryBoundManifestOr
        => Get(nameof(ReleaseDiscoveryTheReleaseIsMissingItsRepositoryBoundManifestOr));

    /// <summary>
    /// Gets the localized text: The release service could not be reached. Check your internet connection and try again.
    /// </summary>
    public static string ReleaseDiscoveryTheReleaseServiceCouldNotBeReachedCheckYour
        => Get(nameof(ReleaseDiscoveryTheReleaseServiceCouldNotBeReachedCheckYour));

    /// <summary>
    /// Gets the localized text: The release tag is missing.
    /// </summary>
    public static string ReleaseDiscoveryTheReleaseTagIsMissing
        => Get(nameof(ReleaseDiscoveryTheReleaseTagIsMissing));

    /// <summary>
    /// Gets the localized text: The metadata container is unsupported or oversized.
    /// </summary>
    public static string ReleaseDownloadStoreTheMetadataContainerIsUnsupportedOrOversized
        => Get(nameof(ReleaseDownloadStoreTheMetadataContainerIsUnsupportedOrOversized));

    /// <summary>
    /// Gets the localized text: The release download failed its signed size or SHA-256 check.
    /// </summary>
    public static string ReleaseDownloadStoreTheReleaseDownloadFailedItsSignedSizeOrSHA
        => Get(nameof(ReleaseDownloadStoreTheReleaseDownloadFailedItsSignedSizeOrSHA));

    /// <summary>
    /// Gets the localized text: An artifact request returned no body.
    /// </summary>
    public static string ReleaseHttpClientAnArtifactRequestReturnedNoBody
        => Get(nameof(ReleaseHttpClientAnArtifactRequestReturnedNoBody));

    /// <summary>
    /// Gets the localized text: Release checking is temporarily rate limited.
    /// </summary>
    public static string ReleaseHttpClientReleaseCheckingIsTemporarilyRateLimited
        => Get(nameof(ReleaseHttpClientReleaseCheckingIsTemporarilyRateLimited));

    /// <summary>
    /// Gets the localized text: The fixed release API must not redirect to another repository.
    /// </summary>
    public static string ReleaseHttpClientTheFixedReleaseAPIMustNotRedirectToAnother
        => Get(nameof(ReleaseHttpClientTheFixedReleaseAPIMustNotRedirectToAnother));

    /// <summary>
    /// Gets the localized text: The release redirect has no destination.
    /// </summary>
    public static string ReleaseHttpClientTheReleaseRedirectHasNoDestination
        => Get(nameof(ReleaseHttpClientTheReleaseRedirectHasNoDestination));

    /// <summary>
    /// Gets the localized text: The release request exceeded its retry or redirect budget.
    /// </summary>
    public static string ReleaseHttpClientTheReleaseRequestExceededItsRetryOrRedirectBudget
        => Get(nameof(ReleaseHttpClientTheReleaseRequestExceededItsRetryOrRedirectBudget));

    /// <summary>
    /// Gets the localized text: The release request or redirect is outside the fixed repository and allowed asset hosts.
    /// </summary>
    public static string ReleaseHttpClientTheReleaseRequestOrRedirectIsOutsideTheFixed
        => Get(nameof(ReleaseHttpClientTheReleaseRequestOrRedirectIsOutsideTheFixed));

    /// <summary>
    /// Gets the localized text: The release response exceeds its size limit.
    /// </summary>
    public static string ReleaseHttpClientTheReleaseResponseExceedsItsSizeLimit
        => Get(nameof(ReleaseHttpClientTheReleaseResponseExceedsItsSizeLimit));

    /// <summary>
    /// Gets the localized text: The release URL is not a credential-free public HTTPS address.
    /// </summary>
    public static string ReleaseHttpClientTheReleaseURLIsNotACredentialFreePublic
        => Get(nameof(ReleaseHttpClientTheReleaseURLIsNotACredentialFreePublic));

    /// <summary>
    /// Gets the localized text: Duplicate release document properties are not allowed.
    /// </summary>
    public static string ReleaseJsonDuplicateReleaseDocumentPropertiesAreNotAllowed
        => Get(nameof(ReleaseJsonDuplicateReleaseDocumentPropertiesAreNotAllowed));

    /// <summary>
    /// Gets the localized text: Release document arrays must not contain null entries.
    /// </summary>
    public static string ReleaseJsonReleaseDocumentArraysMustNotContainNullEntries
        => Get(nameof(ReleaseJsonReleaseDocumentArraysMustNotContainNullEntries));

    /// <summary>
    /// Gets the localized text: The release document is empty.
    /// </summary>
    public static string ReleaseJsonTheReleaseDocumentIsEmpty
        => Get(nameof(ReleaseJsonTheReleaseDocumentIsEmpty));

    /// <summary>
    /// Gets the localized text: The release document is empty, oversized or has an unsupported byte-order mark.
    /// </summary>
    public static string ReleaseJsonTheReleaseDocumentIsEmptyOversizedOrHasAn
        => Get(nameof(ReleaseJsonTheReleaseDocumentIsEmptyOversizedOrHasAn));

    /// <summary>
    /// Gets the localized text: An embedded document differs from its signed identity.
    /// </summary>
    public static string ReleaseMetadataAnEmbeddedDocumentDiffersFromItsSignedIdentity
        => Get(nameof(ReleaseMetadataAnEmbeddedDocumentDiffersFromItsSignedIdentity));

    /// <summary>
    /// Gets the localized text: The asset has no metadata container.
    /// </summary>
    public static string ReleaseMetadataTheAssetHasNoMetadataContainer
        => Get(nameof(ReleaseMetadataTheAssetHasNoMetadataContainer));

    /// <summary>
    /// Gets the localized text: The metadata container differs from its signed identity.
    /// </summary>
    public static string ReleaseMetadataTheMetadataContainerDiffersFromItsSignedIdentity
        => Get(nameof(ReleaseMetadataTheMetadataContainerDiffersFromItsSignedIdentity));

    /// <summary>
    /// Gets the localized text: The metadata container is unsupported, ambiguous, or oversized.
    /// </summary>
    public static string ReleaseMetadataTheMetadataContainerIsUnsupportedAmbiguousOrOversized
        => Get(nameof(ReleaseMetadataTheMetadataContainerIsUnsupportedAmbiguousOrOversized));

    /// <summary>
    /// Gets the localized text: The signed document is missing from its metadata container.
    /// </summary>
    public static string ReleaseMetadataTheSignedDocumentIsMissingFromItsMetadataContainer
        => Get(nameof(ReleaseMetadataTheSignedDocumentIsMissingFromItsMetadataContainer));

    /// <summary>
    /// Gets the localized text: The helper asset name does not declare a supported version.
    /// </summary>
    public static string ReleaseProtocolTheHelperAssetNameDoesNotDeclareASupported
        => Get(nameof(ReleaseProtocolTheHelperAssetNameDoesNotDeclareASupported));

    /// <summary>
    /// Gets the localized text: The release asset name is unsafe.
    /// </summary>
    public static string ReleaseProtocolTheReleaseAssetNameIsUnsafe
        => Get(nameof(ReleaseProtocolTheReleaseAssetNameIsUnsafe));

    /// <summary>
    /// Gets the localized text: The release contains an invalid content fingerprint.
    /// </summary>
    public static string ReleaseProtocolTheReleaseContainsAnInvalidContentFingerprint
        => Get(nameof(ReleaseProtocolTheReleaseContainsAnInvalidContentFingerprint));

    /// <summary>
    /// Gets the localized text: The release game commit is invalid.
    /// </summary>
    public static string ReleaseProtocolTheReleaseGameCommitIsInvalid
        => Get(nameof(ReleaseProtocolTheReleaseGameCommitIsInvalid));

    /// <summary>
    /// Gets the localized text: The release version is not a supported stable numeric version.
    /// </summary>
    public static string ReleaseProtocolTheReleaseVersionIsNotASupportedStableNumeric
        => Get(nameof(ReleaseProtocolTheReleaseVersionIsNotASupportedStableNumeric));

    /// <summary>
    /// Gets the localized text: Each supported commit requires one unambiguous game inventory.
    /// </summary>
    public static string ReleaseVerifierEachSupportedCommitRequiresOneUnambiguousGameInventory
        => Get(nameof(ReleaseVerifierEachSupportedCommitRequiresOneUnambiguousGameInventory));

    /// <summary>
    /// Gets the localized text: No trusted updater signature is available for this release.
    /// </summary>
    public static string ReleaseVerifierNoTrustedUpdaterSignatureIsAvailableForThisRelease
        => Get(nameof(ReleaseVerifierNoTrustedUpdaterSignatureIsAvailableForThisRelease));

    /// <summary>
    /// Gets the localized text: Only replaceable game text files may declare a signed Windows checkout representation.
    /// </summary>
    public static string ReleaseVerifierOnlyReplaceableGameTextFilesMayDeclareASigned
        => Get(nameof(ReleaseVerifierOnlyReplaceableGameTextFilesMayDeclareASigned));

    /// <summary>
    /// Gets the localized text: The asset URL, size or role is outside the signed release contract.
    /// </summary>
    public static string ReleaseVerifierTheAssetURLSizeOrRoleIsOutsideThe
        => Get(nameof(ReleaseVerifierTheAssetURLSizeOrRoleIsOutsideThe));

    /// <summary>
    /// Gets the localized text: The bundled updater is older than the release&apos;s minimum engine.
    /// </summary>
    public static string ReleaseVerifierTheBundledUpdaterIsOlderThanTheReleaseS
        => Get(nameof(ReleaseVerifierTheBundledUpdaterIsOlderThanTheReleaseS));

    /// <summary>
    /// Gets the localized text: The compact release requires one metadata container and an embedded helper.
    /// </summary>
    public static string ReleaseVerifierTheCompactReleaseRequiresOneMetadataContainerAndAn
        => Get(nameof(ReleaseVerifierTheCompactReleaseRequiresOneMetadataContainerAndAn));

    /// <summary>
    /// Gets the localized text: The detached signature document is unsupported or ambiguous.
    /// </summary>
    public static string ReleaseVerifierTheDetachedSignatureDocumentIsUnsupportedOrAmbiguous
        => Get(nameof(ReleaseVerifierTheDetachedSignatureDocumentIsUnsupportedOrAmbiguous));

    /// <summary>
    /// Gets the localized text: The file inventory refers to an unsupported game commit.
    /// </summary>
    public static string ReleaseVerifierTheFileInventoryRefersToAnUnsupportedGameCommit
        => Get(nameof(ReleaseVerifierTheFileInventoryRefersToAnUnsupportedGameCommit));

    /// <summary>
    /// Gets the localized text: The file inventory schema or entry count is unsupported.
    /// </summary>
    public static string ReleaseVerifierTheFileInventorySchemaOrEntryCountIsUnsupported
        => Get(nameof(ReleaseVerifierTheFileInventorySchemaOrEntryCountIsUnsupported));

    /// <summary>
    /// Gets the localized text: The game inventory does not match its signed baseline reference.
    /// </summary>
    public static string ReleaseVerifierTheGameInventoryDoesNotMatchItsSignedBaseline
        => Get(nameof(ReleaseVerifierTheGameInventoryDoesNotMatchItsSignedBaseline));

    /// <summary>
    /// Gets the localized text: The inventory bytes do not match the signed release.
    /// </summary>
    public static string ReleaseVerifierTheInventoryBytesDoNotMatchTheSignedRelease
        => Get(nameof(ReleaseVerifierTheInventoryBytesDoNotMatchTheSignedRelease));

    /// <summary>
    /// Gets the localized text: The inventory contains a file/directory prefix collision.
    /// </summary>
    public static string ReleaseVerifierTheInventoryContainsAFileDirectoryPrefixCollision
        => Get(nameof(ReleaseVerifierTheInventoryContainsAFileDirectoryPrefixCollision));

    /// <summary>
    /// Gets the localized text: The inventory has duplicate paths, excessive size or inconsistent ownership.
    /// </summary>
    public static string ReleaseVerifierTheInventoryHasDuplicatePathsExcessiveSizeOrInconsistent
        => Get(nameof(ReleaseVerifierTheInventoryHasDuplicatePathsExcessiveSizeOrInconsistent));

    /// <summary>
    /// Gets the localized text: The inventory is not referenced by this signed release.
    /// </summary>
    public static string ReleaseVerifierTheInventoryIsNotReferencedByThisSignedRelease
        => Get(nameof(ReleaseVerifierTheInventoryIsNotReferencedByThisSignedRelease));

    /// <summary>
    /// Gets the localized text: The Ironmon inventory has an unsupported scope or flavor.
    /// </summary>
    public static string ReleaseVerifierTheIronmonInventoryHasAnUnsupportedScopeOrFlavor
        => Get(nameof(ReleaseVerifierTheIronmonInventoryHasAnUnsupportedScopeOrFlavor));

    /// <summary>
    /// Gets the localized text: The legacy inventory is not explicitly authorized for this version and flavor.
    /// </summary>
    public static string ReleaseVerifierTheLegacyInventoryIsNotExplicitlyAuthorizedForThis
        => Get(nameof(ReleaseVerifierTheLegacyInventoryIsNotExplicitlyAuthorizedForThis));

    /// <summary>
    /// Gets the localized text: The legacy release requires one separate helper archive.
    /// </summary>
    public static string ReleaseVerifierTheLegacyReleaseRequiresOneSeparateHelperArchive
        => Get(nameof(ReleaseVerifierTheLegacyReleaseRequiresOneSeparateHelperArchive));

    /// <summary>
    /// Gets the localized text: The metadata storage differs from the signed release schema.
    /// </summary>
    public static string ReleaseVerifierTheMetadataStorageDiffersFromTheSignedReleaseSchema
        => Get(nameof(ReleaseVerifierTheMetadataStorageDiffersFromTheSignedReleaseSchema));

    /// <summary>
    /// Gets the localized text: The package inventory version does not match the signed tracker and scripts.
    /// </summary>
    public static string ReleaseVerifierThePackageInventoryVersionDoesNotMatchTheSigned
        => Get(nameof(ReleaseVerifierThePackageInventoryVersionDoesNotMatchTheSigned));

    /// <summary>
    /// Gets the localized text: The package must contain matching tracker, scripts and data without claiming the updater&apos;s local receipt.
    /// </summary>
    public static string ReleaseVerifierThePackageMustContainMatchingTrackerScriptsAndData
        => Get(nameof(ReleaseVerifierThePackageMustContainMatchingTrackerScriptsAndData));

    /// <summary>
    /// Gets the localized text: The release adoption references are invalid or ambiguous.
    /// </summary>
    public static string ReleaseVerifierTheReleaseAdoptionReferencesAreInvalidOrAmbiguous
        => Get(nameof(ReleaseVerifierTheReleaseAdoptionReferencesAreInvalidOrAmbiguous));

    /// <summary>
    /// Gets the localized text: The release asset list is incomplete or ambiguous.
    /// </summary>
    public static string ReleaseVerifierTheReleaseAssetListIsIncompleteOrAmbiguous
        => Get(nameof(ReleaseVerifierTheReleaseAssetListIsIncompleteOrAmbiguous));

    /// <summary>
    /// Gets the localized text: The release contains duplicate or missing required asset roles.
    /// </summary>
    public static string ReleaseVerifierTheReleaseContainsDuplicateOrMissingRequiredAssetRoles
        => Get(nameof(ReleaseVerifierTheReleaseContainsDuplicateOrMissingRequiredAssetRoles));

    /// <summary>
    /// Gets the localized text: The release identity, version, channel or schema is unsupported.
    /// </summary>
    public static string ReleaseVerifierTheReleaseIdentityVersionChannelOrSchemaIsUnsupported
        => Get(nameof(ReleaseVerifierTheReleaseIdentityVersionChannelOrSchemaIsUnsupported));

    /// <summary>
    /// Gets the localized text: The release lacks an authenticated embedded helper.
    /// </summary>
    public static string ReleaseVerifierTheReleaseLacksAnAuthenticatedEmbeddedHelper
        => Get(nameof(ReleaseVerifierTheReleaseLacksAnAuthenticatedEmbeddedHelper));

    /// <summary>
    /// Gets the localized text: The release metadata container is unsupported or oversized.
    /// </summary>
    public static string ReleaseVerifierTheReleaseMetadataContainerIsUnsupportedOrOversized
        => Get(nameof(ReleaseVerifierTheReleaseMetadataContainerIsUnsupportedOrOversized));

    /// <summary>
    /// Gets the localized text: The release must bind both package inventories and its release notes.
    /// </summary>
    public static string ReleaseVerifierTheReleaseMustBindBothPackageInventoriesAndIts
        => Get(nameof(ReleaseVerifierTheReleaseMustBindBothPackageInventoriesAndIts));

    /// <summary>
    /// Gets the localized text: The release references an invalid legacy package inventory.
    /// </summary>
    public static string ReleaseVerifierTheReleaseReferencesAnInvalidLegacyPackageInventory
        => Get(nameof(ReleaseVerifierTheReleaseReferencesAnInvalidLegacyPackageInventory));

    /// <summary>
    /// Gets the localized text: The release signature algorithm or key identifier is invalid.
    /// </summary>
    public static string ReleaseVerifierTheReleaseSignatureAlgorithmOrKeyIdentifierIsInvalid
        => Get(nameof(ReleaseVerifierTheReleaseSignatureAlgorithmOrKeyIdentifierIsInvalid));

    /// <summary>
    /// Gets the localized text: The release signature is not canonical IEEE P1363 data.
    /// </summary>
    public static string ReleaseVerifierTheReleaseSignatureIsNotCanonicalIEEEP1363Data
        => Get(nameof(ReleaseVerifierTheReleaseSignatureIsNotCanonicalIEEEP1363Data));

    /// <summary>
    /// Gets the localized text: The signed game compatibility policy is invalid.
    /// </summary>
    public static string ReleaseVerifierTheSignedGameCompatibilityPolicyIsInvalid
        => Get(nameof(ReleaseVerifierTheSignedGameCompatibilityPolicyIsInvalid));

    /// <summary>
    /// Gets the localized text: The update manifest has no valid signature from an embedded updater key.
    /// </summary>
    public static string ReleaseVerifierTheUpdateManifestHasNoValidSignatureFromAn
        => Get(nameof(ReleaseVerifierTheUpdateManifestHasNoValidSignatureFromAn));

    /// <summary>
    /// Gets the localized text: Updater trust requires dedicated P-256 public keys.
    /// </summary>
    public static string ReleaseVerifierUpdaterTrustRequiresDedicatedP256PublicKeys
        => Get(nameof(ReleaseVerifierUpdaterTrustRequiresDedicatedP256PublicKeys));

    /// <summary>
    /// Gets the localized text: The approved branch name is not supported.
    /// </summary>
    public static string RepositoryPolicyTheApprovedBranchNameIsNotSupported
        => Get(nameof(RepositoryPolicyTheApprovedBranchNameIsNotSupported));

    /// <summary>
    /// Gets the localized text: The approved upstream must be a public HTTPS repository.
    /// </summary>
    public static string RepositoryPolicyTheApprovedUpstreamMustBeAPublicHTTPSRepository
        => Get(nameof(RepositoryPolicyTheApprovedUpstreamMustBeAPublicHTTPSRepository));

    /// <summary>
    /// Gets the localized text: The repository configuration is ambiguous or unsupported.
    /// </summary>
    public static string RepositoryPolicyTheRepositoryConfigurationIsAmbiguousOrUnsupported
        => Get(nameof(RepositoryPolicyTheRepositoryConfigurationIsAmbiguousOrUnsupported));

    /// <summary>
    /// Gets the localized text: The repository configuration is too large.
    /// </summary>
    public static string RepositoryPolicyTheRepositoryConfigurationIsTooLarge
        => Get(nameof(RepositoryPolicyTheRepositoryConfigurationIsTooLarge));

    /// <summary>
    /// Gets the localized text: The repository contains unapproved Git configuration.
    /// </summary>
    public static string RepositoryPolicyTheRepositoryContainsUnapprovedGitConfiguration
        => Get(nameof(RepositoryPolicyTheRepositoryContainsUnapprovedGitConfiguration));

    /// <summary>
    /// Gets the localized text: The repository has unsupported configuration sections.
    /// </summary>
    public static string RepositoryPolicyTheRepositoryHasUnsupportedConfigurationSections
        => Get(nameof(RepositoryPolicyTheRepositoryHasUnsupportedConfigurationSections));

    /// <summary>
    /// Gets the localized text: The repository is missing expected remote or branch configuration.
    /// </summary>
    public static string RepositoryPolicyTheRepositoryIsMissingExpectedRemoteOrBranchConfiguration
        => Get(nameof(RepositoryPolicyTheRepositoryIsMissingExpectedRemoteOrBranchConfiguration));

    /// <summary>
    /// Gets the localized text: An interrupted installation needs recovery before Setup can continue.
    /// </summary>
    public static string SetupPreparationAnInterruptedInstallationNeedsRecoveryBeforeSetupCanContinue
        => Get(nameof(SetupPreparationAnInterruptedInstallationNeedsRecoveryBeforeSetupCanContinue));

    /// <summary>
    /// Gets the localized text: Choose an installation folder, not a drive root or a file.
    /// </summary>
    public static string SetupPreparationChooseAnInstallationFolderNotADriveRootOr
        => Get(nameof(SetupPreparationChooseAnInstallationFolderNotADriveRootOr));

    /// <summary>
    /// Gets the localized text: The installed tracker version could not be identified. Restore its original package before installing.
    /// </summary>
    public static string SetupPreparationTheInstalledTrackerVersionCouldNotBeIdentifiedRestore
        => Get(nameof(SetupPreparationTheInstalledTrackerVersionCouldNotBeIdentifiedRestore));

    /// <summary>
    /// Gets the localized text: This game folder does not match one supported game version. Restore the original game files before installing.
    /// </summary>
    public static string SetupPreparationThisGameFolderDoesNotMatchOneSupportedGame
        => Get(nameof(SetupPreparationThisGameFolderDoesNotMatchOneSupportedGame));

    /// <summary>
    /// Gets the localized text: This Ironmon version is already installed with a different game. Use a matching newer release; Setup will not downgrade the game.
    /// </summary>
    public static string SetupPreparationThisIronmonVersionIsAlreadyInstalledWithADifferent
        => Get(nameof(SetupPreparationThisIronmonVersionIsAlreadyInstalledWithADifferent));

    /// <summary>
    /// Gets the localized text: This release changes the game version. Finish your Ironmon run before continuing. Closing the game does not finish a run.
    /// </summary>
    public static string SetupPreparationThisReleaseChangesTheGameVersionFinishYourIronmon
        => Get(nameof(SetupPreparationThisReleaseChangesTheGameVersionFinishYourIronmon));

    /// <summary>
    /// Gets the localized text: This release needs a newer Ironmon Setup. Download its current installer before making changes.
    /// </summary>
    public static string SetupPreparationThisReleaseNeedsANewerIronmonSetupDownloadIts
        => Get(nameof(SetupPreparationThisReleaseNeedsANewerIronmonSetupDownloadIts));

    /// <summary>
    /// Gets the localized text: This release requires a finished Ironmon run. Finish your current run before continuing.
    /// </summary>
    public static string SetupPreparationThisReleaseRequiresAFinishedIronmonRunFinishYour
        => Get(nameof(SetupPreparationThisReleaseRequiresAFinishedIronmonRunFinishYour));

    /// <summary>
    /// Gets the localized text: Ironmon Setup
    /// </summary>
    public static string SetupProgramIronmonSetup
        => Get(nameof(SetupProgramIronmonSetup));

    /// <summary>
    /// Gets the localized text: An interrupted installation needs recovery. Its backups have been kept.
    /// </summary>
    public static string SetupSessionAnInterruptedInstallationNeedsRecoveryItsBackupsHaveBeen
        => Get(nameof(SetupSessionAnInterruptedInstallationNeedsRecoveryItsBackupsHaveBeen));

    /// <summary>
    /// Gets the localized text: Cancelled safely. Review the installation before trying again.
    /// </summary>
    public static string SetupSessionCancelledSafelyReviewTheInstallationBeforeTryingAgain
        => Get(nameof(SetupSessionCancelledSafelyReviewTheInstallationBeforeTryingAgain));

    /// <summary>
    /// Gets the localized text: Checking the latest release and your installation…
    /// </summary>
    public static string SetupSessionCheckingTheLatestReleaseAndYourInstallation
        => Get(nameof(SetupSessionCheckingTheLatestReleaseAndYourInstallation));

    /// <summary>
    /// Gets the localized text: Choose where to install Ironmon.
    /// </summary>
    public static string SetupSessionChooseWhereToInstallIronmon
        => Get(nameof(SetupSessionChooseWhereToInstallIronmon));

    /// <summary>
    /// Gets the localized text: Choose your installation options.
    /// </summary>
    public static string SetupSessionChooseYourInstallationOptions
        => Get(nameof(SetupSessionChooseYourInstallationOptions));

    /// <summary>
    /// Gets the localized text: Closing the game and tracker. Complete any game save prompt to continue…
    /// </summary>
    public static string SetupSessionClosingTheGameAndTrackerCompleteAnyGameSave
        => Get(nameof(SetupSessionClosingTheGameAndTrackerCompleteAnyGameSave));

    /// <summary>
    /// Gets the localized text: Downloading and verifying your installation…
    /// </summary>
    public static string SetupSessionDownloadingAndVerifyingYourInstallation
        => Get(nameof(SetupSessionDownloadingAndVerifyingYourInstallation));

    /// <summary>
    /// Gets the localized text: Finish your run before continuing.
    /// </summary>
    public static string SetupSessionFinishYourRunBeforeContinuing
        => Get(nameof(SetupSessionFinishYourRunBeforeContinuing));

    /// <summary>
    /// Gets the localized text: Installation did not finish. Your previous files were restored or retained for recovery.
    /// </summary>
    public static string SetupSessionInstallationDidNotFinishYourPreviousFilesWereRestored
        => Get(nameof(SetupSessionInstallationDidNotFinishYourPreviousFilesWereRestored));

    /// <summary>
    /// Gets the localized text: Installing and verifying files…
    /// </summary>
    public static string SetupSessionInstallingAndVerifyingFiles
        => Get(nameof(SetupSessionInstallingAndVerifyingFiles));

    /// <summary>
    /// Gets the localized text: Installing Microsoft WebView2. Windows may ask for permission…
    /// </summary>
    public static string SetupSessionInstallingMicrosoftWebView2WindowsMayAskForPermission
        => Get(nameof(SetupSessionInstallingMicrosoftWebView2WindowsMayAskForPermission));

    /// <summary>
    /// Gets the localized text: Ironmon is installed, but the desktop shortcut could not be created.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledButTheDesktopShortcutCouldNot
        => Get(nameof(SetupSessionIronmonIsInstalledButTheDesktopShortcutCouldNot));

    /// <summary>
    /// Gets the localized text: Ironmon is installed. Downloading the optional sprite library…
    /// </summary>
    public static string SetupSessionIronmonIsInstalledDownloadingTheOptionalSpriteLibrary
        => Get(nameof(SetupSessionIronmonIsInstalledDownloadingTheOptionalSpriteLibrary));

    /// <summary>
    /// Gets the localized text: Ironmon is installed; optional work is incomplete.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledOptionalWorkIsIncomplete
        => Get(nameof(SetupSessionIronmonIsInstalledOptionalWorkIsIncomplete));

    /// <summary>
    /// Gets the localized text: Ironmon is installed. Remaining optional work can continue in the tracker.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledRemainingOptionalWorkCanContinueIn
        => Get(nameof(SetupSessionIronmonIsInstalledRemainingOptionalWorkCanContinueIn));

    /// <summary>
    /// Gets the localized text: Ironmon is installed. Some sprite files could not be downloaded. Retry, or finish and continue in the tracker later.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledSomeSpriteFilesCouldNotBe
        => Get(nameof(SetupSessionIronmonIsInstalledSomeSpriteFilesCouldNotBe));

    /// <summary>
    /// Gets the localized text: Ironmon is installed; the last action needs attention.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledTheLastActionNeedsAttention
        => Get(nameof(SetupSessionIronmonIsInstalledTheLastActionNeedsAttention));

    /// <summary>
    /// Gets the localized text: Ironmon is installed. You can open the tracker and discard Setup.
    /// </summary>
    public static string SetupSessionIronmonIsInstalledYouCanOpenTheTrackerAnd
        => Get(nameof(SetupSessionIronmonIsInstalledYouCanOpenTheTrackerAnd));

    /// <summary>
    /// Gets the localized text: Microsoft WebView2 could not be detected after installation. Retry the prerequisite before continuing.
    /// </summary>
    public static string SetupSessionMicrosoftWebView2CouldNotBeDetectedAfterInstallationRetry
        => Get(nameof(SetupSessionMicrosoftWebView2CouldNotBeDetectedAfterInstallationRetry));

    /// <summary>
    /// Gets the localized text: No interrupted installation was selected.
    /// </summary>
    public static string SetupSessionNoInterruptedInstallationWasSelected
        => Get(nameof(SetupSessionNoInterruptedInstallationWasSelected));

    /// <summary>
    /// Gets the localized text: No supported signed release is available yet. Try again later.
    /// </summary>
    public static string SetupSessionNoSupportedSignedReleaseIsAvailableYetTryAgain
        => Get(nameof(SetupSessionNoSupportedSignedReleaseIsAvailableYetTryAgain));

    /// <summary>
    /// Gets the localized text: Recovery completed.
    /// </summary>
    public static string SetupSessionRecoveryCompleted
        => Get(nameof(SetupSessionRecoveryCompleted));

    /// <summary>
    /// Gets the localized text: Recovery needs attention. Your backups have been kept.
    /// </summary>
    public static string SetupSessionRecoveryNeedsAttentionYourBackupsHaveBeenKept
        => Get(nameof(SetupSessionRecoveryNeedsAttentionYourBackupsHaveBeenKept));

    /// <summary>
    /// Gets the localized text: Restoring your previous files…
    /// </summary>
    public static string SetupSessionRestoringYourPreviousFiles
        => Get(nameof(SetupSessionRestoringYourPreviousFiles));

    /// <summary>
    /// Gets the localized text: Review the installation folder first.
    /// </summary>
    public static string SetupSessionReviewTheInstallationFolderFirst
        => Get(nameof(SetupSessionReviewTheInstallationFolderFirst));

    /// <summary>
    /// Gets the localized text: Review your installation and options, then choose Install.
    /// </summary>
    public static string SetupSessionReviewYourInstallationAndOptionsThenChooseInstall
        => Get(nameof(SetupSessionReviewYourInstallationAndOptionsThenChooseInstall));

    /// <summary>
    /// Gets the localized text: Select the Microsoft WebView2 prerequisite before installing. The tracker needs it to open.
    /// </summary>
    public static string SetupSessionSelectTheMicrosoftWebView2PrerequisiteBeforeInstallingTheTracker
        => Get(nameof(SetupSessionSelectTheMicrosoftWebView2PrerequisiteBeforeInstallingTheTracker));

    /// <summary>
    /// Gets the localized text: Setup could not complete this step.
    /// </summary>
    public static string SetupSessionSetupCouldNotCompleteThisStep
        => Get(nameof(SetupSessionSetupCouldNotCompleteThisStep));

    /// <summary>
    /// Gets the localized text: Setup will keep your installed tracker package.
    /// </summary>
    public static string SetupSessionSetupWillKeepYourInstalledTrackerPackage
        => Get(nameof(SetupSessionSetupWillKeepYourInstalledTrackerPackage));

    /// <summary>
    /// Gets the localized text: Your core installation is current. You can add the optional items below.
    /// </summary>
    public static string SetupSessionYourCoreInstallationIsCurrentYouCanAddThe
        => Get(nameof(SetupSessionYourCoreInstallationIsCurrentYouCanAddThe));

    /// <summary>
    /// Gets the localized text: Your previous files were restored. Review the folder again to install.
    /// </summary>
    public static string SetupSessionYourPreviousFilesWereRestoredReviewTheFolderAgain
        => Get(nameof(SetupSessionYourPreviousFilesWereRestoredReviewTheFolderAgain));

    /// <summary>
    /// Gets the localized text: 1  Location
    /// </summary>
    public static string SetupWindow1Location
        => Get(nameof(SetupWindow1Location));

    /// <summary>
    /// Gets the localized text: 2  Options
    /// </summary>
    public static string SetupWindow2Options
        => Get(nameof(SetupWindow2Options));

    /// <summary>
    /// Gets the localized text: 3  Install
    /// </summary>
    public static string SetupWindow3Install
        => Get(nameof(SetupWindow3Install));

    /// <summary>
    /// Gets the localized text: 4  Ready
    /// </summary>
    public static string SetupWindow4Ready
        => Get(nameof(SetupWindow4Ready));

    /// <summary>
    /// Gets the localized text: Back
    /// </summary>
    public static string SetupWindowBack
        => Get(nameof(SetupWindowBack));

    /// <summary>
    /// Gets the localized text: Back up and replace this file
    /// </summary>
    public static string SetupWindowBackUpAndReplaceThisFile
        => Get(nameof(SetupWindowBackUpAndReplaceThisFile));

    /// <summary>
    /// Gets the localized text: Browse…
    /// </summary>
    public static string SetupWindowBrowse
        => Get(nameof(SetupWindowBrowse));

    /// <summary>
    /// Gets the localized text: Cancel
    /// </summary>
    public static string SetupWindowCancel
        => Get(nameof(SetupWindowCancel));

    /// <summary>
    /// Gets the localized text: Check again
    /// </summary>
    public static string SetupWindowCheckAgain
        => Get(nameof(SetupWindowCheckAgain));

    /// <summary>
    /// Gets the localized text: Checking your installation
    /// </summary>
    public static string SetupWindowCheckingYourInstallation
        => Get(nameof(SetupWindowCheckingYourInstallation));

    /// <summary>
    /// Gets the localized text: Choose an empty folder for a new installation, or select your existing Infinite Fusion folder.
    /// </summary>
    public static string SetupWindowChooseAnEmptyFolderForANewInstallationOr
        => Get(nameof(SetupWindowChooseAnEmptyFolderForANewInstallationOr));

    /// <summary>
    /// Gets the localized text: Choose the Infinite Fusion installation folder
    /// </summary>
    public static string SetupWindowChooseTheInfiniteFusionInstallationFolder
        => Get(nameof(SetupWindowChooseTheInfiniteFusionInstallationFolder));

    /// <summary>
    /// Gets the localized text: Choose your game folder
    /// </summary>
    public static string SetupWindowChooseYourGameFolder
        => Get(nameof(SetupWindowChooseYourGameFolder));

    /// <summary>
    /// Gets the localized text: Continue
    /// </summary>
    public static string SetupWindowContinue
        => Get(nameof(SetupWindowContinue));

    /// <summary>
    /// Gets the localized text: Create a desktop shortcut
    /// </summary>
    public static string SetupWindowCreateADesktopShortcut
        => Get(nameof(SetupWindowCreateADesktopShortcut));

    /// <summary>
    /// Gets the localized text: Desktop tracker shortcut: not selected.
    /// </summary>
    public static string SetupWindowDesktopTrackerShortcutNotSelected
        => Get(nameof(SetupWindowDesktopTrackerShortcutNotSelected));

    /// <summary>
    /// Gets the localized text: Desktop tracker shortcut: selected.
    /// </summary>
    public static string SetupWindowDesktopTrackerShortcutSelected
        => Get(nameof(SetupWindowDesktopTrackerShortcutSelected));

    /// <summary>
    /// Gets the localized text: Downloaded from Microsoft after you choose Install. Windows may ask for permission.
    /// </summary>
    public static string SetupWindowDownloadedFromMicrosoftAfterYouChooseInstallWindowsMay
        => Get(nameof(SetupWindowDownloadedFromMicrosoftAfterYouChooseInstallWindowsMay));

    /// <summary>
    /// Gets the localized text: Download sprite sheets
    /// </summary>
    public static string SetupWindowDownloadSpriteSheets
        => Get(nameof(SetupWindowDownloadSpriteSheets));

    /// <summary>
    /// Gets the localized text: File {0} of {1}
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <param name="value1">The value for resource placeholder 1.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string SetupWindowFileOf(object? value0, object? value1)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupWindowFileOf)), value0, value1);

    /// <summary>
    /// Gets the localized text: Finish
    /// </summary>
    public static string SetupWindowFinish
        => Get(nameof(SetupWindowFinish));

    /// <summary>
    /// Gets the localized text: Finish setup
    /// </summary>
    public static string SetupWindowFinishSetup
        => Get(nameof(SetupWindowFinishSetup));

    /// <summary>
    /// Gets the localized text: Finish your run first
    /// </summary>
    public static string SetupWindowFinishYourRunFirst
        => Get(nameof(SetupWindowFinishYourRunFirst));

    /// <summary>
    /// Gets the localized text: For an existing game, select the folder containing InfiniteFusion2.exe.
    /// </summary>
    public static string SetupWindowForAnExistingGameSelectTheFolderContainingInfiniteFusion2
        => Get(nameof(SetupWindowForAnExistingGameSelectTheFolderContainingInfiniteFusion2));

    /// <summary>
    /// Gets the localized text: Go back to Options and select the required Microsoft WebView2 installation to continue.
    /// </summary>
    public static string SetupWindowGoBackToOptionsAndSelectTheRequiredMicrosoft
        => Get(nameof(SetupWindowGoBackToOptionsAndSelectTheRequiredMicrosoft));

    /// <summary>
    /// Gets the localized text: Install
    /// </summary>
    public static string SetupWindowInstall
        => Get(nameof(SetupWindowInstall));

    /// <summary>
    /// Gets the localized text: Installation folder
    /// </summary>
    public static string SetupWindowInstallationFolder
        => Get(nameof(SetupWindowInstallationFolder));

    /// <summary>
    /// Gets the localized text: INSTALLATION FOLDER
    /// </summary>
    public static string SetupWindowFolderSection
        => Get(nameof(SetupWindowFolderSection));

    /// <summary>
    /// Gets the localized text: Install Microsoft WebView2, required by the tracker
    /// </summary>
    public static string SetupWindowInstallMicrosoftWebView2RequiredByTheTracker
        => Get(nameof(SetupWindowInstallMicrosoftWebView2RequiredByTheTracker));

    /// <summary>
    /// Gets the localized text: Ironmon {0} · Infinite Fusion {1}
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <param name="value1">The value for resource placeholder 1.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string SetupWindowIronmonInfiniteFusion(object? value0, object? value1)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupWindowIronmonInfiniteFusion)), value0, value1);

    /// <summary>
    /// Gets the localized text: Ironmon Setup
    /// </summary>
    public static string SetupWindowIronmonSetup
        => Get(nameof(SetupWindowIronmonSetup));

    /// <summary>
    /// Gets the localized text: IRONMON  /  SETUP
    /// </summary>
    public static string SetupWindowBrand
        => Get(nameof(SetupWindowBrand));

    /// <summary>
    /// Gets the localized text: Large optional download. You can also download sprites later in the tracker.
    /// </summary>
    public static string SetupWindowLargeOptionalDownloadYouCanAlsoDownloadSpritesLater
        => Get(nameof(SetupWindowLargeOptionalDownloadYouCanAlsoDownloadSpritesLater));

    /// <summary>
    /// Gets the localized text: Make it yours
    /// </summary>
    public static string SetupWindowMakeItYours
        => Get(nameof(SetupWindowMakeItYours));

    /// <summary>
    /// Gets the localized text: Microsoft WebView2: install the required runtime.
    /// </summary>
    public static string SetupWindowMicrosoftWebView2InstallTheRequiredRuntime
        => Get(nameof(SetupWindowMicrosoftWebView2InstallTheRequiredRuntime));

    /// <summary>
    /// Gets the localized text: My Ironmon run is finished
    /// </summary>
    public static string SetupWindowMyIronmonRunIsFinished
        => Get(nameof(SetupWindowMyIronmonRunIsFinished));

    /// <summary>
    /// Gets the localized text: Next file
    /// </summary>
    public static string SetupWindowNextFile
        => Get(nameof(SetupWindowNextFile));

    /// <summary>
    /// Gets the localized text: Open tracker
    /// </summary>
    public static string SetupWindowOpenTracker
        => Get(nameof(SetupWindowOpenTracker));

    /// <summary>
    /// Gets the localized text: Optional sprite library: download later in the tracker.
    /// </summary>
    public static string SetupWindowOptionalSpriteLibraryDownloadLaterInTheTracker
        => Get(nameof(SetupWindowOptionalSpriteLibraryDownloadLaterInTheTracker));

    /// <summary>
    /// Gets the localized text: Optional sprite library: selected (size unknown).
    /// </summary>
    public static string SetupWindowOptionalSpriteLibrarySelectedSizeUnknown
        => Get(nameof(SetupWindowOptionalSpriteLibrarySelectedSizeUnknown));

    /// <summary>
    /// Gets the localized text: Optional work is incomplete. Retry now, or finish and continue the sprite library from the tracker later. Your core installation is already complete.
    /// </summary>
    public static string SetupWindowOptionalWorkIsIncompleteRetryNowOrFinishAnd
        => Get(nameof(SetupWindowOptionalWorkIsIncompleteRetryNowOrFinishAnd));

    /// <summary>
    /// Gets the localized text: Previous file
    /// </summary>
    public static string SetupWindowPreviousFile
        => Get(nameof(SetupWindowPreviousFile));

    /// <summary>
    /// Gets the localized text: Ready to install
    /// </summary>
    public static string SetupWindowReadyToInstall
        => Get(nameof(SetupWindowReadyToInstall));

    /// <summary>
    /// Gets the localized text: Recommended. No separate .NET installation needed.
    /// </summary>
    public static string SetupWindowRecommendedNoSeparateNETInstallationNeeded
        => Get(nameof(SetupWindowRecommendedNoSeparateNETInstallationNeeded));

    /// <summary>
    /// Gets the localized text: Recover installation
    /// </summary>
    public static string SetupWindowRecoverInstallation
        => Get(nameof(SetupWindowRecoverInstallation));

    /// <summary>
    /// Gets the localized text: Recovery restores a complete installation from its verified backups. Save your game; recovery will close the game and tracker.
    /// </summary>
    public static string SetupWindowRecoveryRestoresACompleteInstallationFromItsVerifiedBackups
        => Get(nameof(SetupWindowRecoveryRestoresACompleteInstallationFromItsVerifiedBackups));

    /// <summary>
    /// Gets the localized text: Retry optional work
    /// </summary>
    public static string SetupWindowRetryOptionalWork
        => Get(nameof(SetupWindowRetryOptionalWork));

    /// <summary>
    /// Gets the localized text: Review changed files
    /// </summary>
    public static string SetupWindowReviewChangedFiles
        => Get(nameof(SetupWindowReviewChangedFiles));

    /// <summary>
    /// Gets the localized text: Review changed files ({0})
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string SetupWindowChangedFilesCount(object? value0)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupWindowChangedFilesCount)), value0);

    /// <summary>
    /// Gets the localized text: Review installation
    /// </summary>
    public static string SetupWindowReviewInstallation
        => Get(nameof(SetupWindowReviewInstallation));

    /// <summary>
    /// Gets the localized text: Runtime included
    /// </summary>
    public static string SetupWindowRuntimeIncluded
        => Get(nameof(SetupWindowRuntimeIncluded));

    /// <summary>
    /// Gets the localized text: Runtime required
    /// </summary>
    public static string SetupWindowRuntimeRequired
        => Get(nameof(SetupWindowRuntimeRequired));

    /// <summary>
    /// Gets the localized text: Save your game before installing. Setup will close the game and tracker and wait for any game save prompt. Installation continues automatically in this window.
    /// </summary>
    public static string SetupWindowSaveYourGameBeforeInstallingSetupWillCloseThe
        => Get(nameof(SetupWindowSaveYourGameBeforeInstallingSetupWillCloseThe));

    /// <summary>
    /// Gets the localized text: Setup keeps the package used by this installation.
    /// </summary>
    public static string SetupWindowSetupKeepsThePackageUsedByThisInstallation
        => Get(nameof(SetupWindowSetupKeepsThePackageUsedByThisInstallation));

    /// <summary>
    /// Gets the localized text: {0} / {1} sheets.
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <param name="value1">The value for resource placeholder 1.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string SetupWindowSheets(object? value0, object? value1)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupWindowSheets)), value0, value1);

    /// <summary>
    /// Gets the localized text: Smaller download. Requires the matching .NET runtime.
    /// </summary>
    public static string SetupWindowSmallerDownloadRequiresTheMatchingNETRuntime
        => Get(nameof(SetupWindowSmallerDownloadRequiresTheMatchingNETRuntime));

    /// <summary>
    /// Gets the localized text: The game and its support files are additional; their transfer size is not known in advance.
    /// </summary>
    public static string SetupWindowTheGameAndItsSupportFilesAreAdditionalTheir
        => Get(nameof(SetupWindowTheGameAndItsSupportFilesAreAdditionalTheir));

    /// <summary>
    /// Gets the localized text: The suggested location installs for your Windows account. Windows may ask for permission if you choose a protected folder.
    /// </summary>
    public static string SetupWindowTheSuggestedLocationInstallsForYourWindowsAccountWindows
        => Get(nameof(SetupWindowTheSuggestedLocationInstallsForYourWindowsAccountWindows));

    /// <summary>
    /// Gets the localized text: This file differs from the released version. Setup will keep a backup before replacing it.
    /// </summary>
    public static string SetupWindowThisFileDiffersFromTheReleasedVersionSetupWill
        => Get(nameof(SetupWindowThisFileDiffersFromTheReleasedVersionSetupWill));

    /// <summary>
    /// Gets the localized text: This file or folder prevents installation. Move it out of the way, then return to the installation review and choose Check again.
    /// </summary>
    public static string SetupWindowThisFileOrFolderPreventsInstallationMoveItOut
        => Get(nameof(SetupWindowThisFileOrFolderPreventsInstallationMoveItOut));

    /// <summary>
    /// Gets the localized text: Tracker and updater: {0} MiB.
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string SetupWindowTrackerAndUpdaterMiB(object? value0)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupWindowTrackerAndUpdaterMiB)), value0);

    /// <summary>
    /// Gets the localized text: Tracker package:
    /// </summary>
    public static string SetupWindowTrackerPackage
        => Get(nameof(SetupWindowTrackerPackage));

    /// <summary>
    /// Gets the localized text: TRACKER PACKAGE
    /// </summary>
    public static string SetupWindowPackageSection
        => Get(nameof(SetupWindowPackageSection));

    /// <summary>
    /// Gets the localized text: Unable to review installation
    /// </summary>
    public static string SetupWindowUnableToReviewInstallation
        => Get(nameof(SetupWindowUnableToReviewInstallation));

    /// <summary>
    /// Gets the localized text: You’re ready to play
    /// </summary>
    public static string SetupWindowYouReReadyToPlay
        => Get(nameof(SetupWindowYouReReadyToPlay));

    /// <summary>
    /// Gets the localized text: Your game and Ironmon are installed. Future updates and sprite downloads are available in the tracker. You can delete the downloaded Setup after closing this window.
    /// </summary>
    public static string SetupWindowYourGameAndIronmonAreInstalledFutureUpdatesAnd
        => Get(nameof(SetupWindowYourGameAndIronmonAreInstalledFutureUpdatesAnd));

    /// <summary>
    /// Gets the localized text: A combined release must contain the early game startup guard.
    /// </summary>
    public static string SignedIronmonAuthorityACombinedReleaseMustContainTheEarlyGameStartup
        => Get(nameof(SignedIronmonAuthorityACombinedReleaseMustContainTheEarlyGameStartup));

    /// <summary>
    /// Gets the localized text: A fresh installation cannot claim ownership from a prior release.
    /// </summary>
    public static string SignedIronmonAuthorityAFreshInstallationCannotClaimOwnershipFromAPrior
        => Get(nameof(SignedIronmonAuthorityAFreshInstallationCannotClaimOwnershipFromAPrior));

    /// <summary>
    /// Gets the localized text: A new game transaction must originate from an empty destination.
    /// </summary>
    public static string SignedIronmonAuthorityANewGameTransactionMustOriginateFromAnEmpty
        => Get(nameof(SignedIronmonAuthorityANewGameTransactionMustOriginateFromAnEmpty));

    /// <summary>
    /// Gets the localized text: Combined game verification is not configured in this updater host.
    /// </summary>
    public static string SignedIronmonAuthorityCombinedGameVerificationIsNotConfiguredInThisUpdater
        => Get(nameof(SignedIronmonAuthorityCombinedGameVerificationIsNotConfiguredInThisUpdater));

    /// <summary>
    /// Gets the localized text: Finish the active run before changing its game version. Combined updates must select a different approved forward commit.
    /// </summary>
    public static string SignedIronmonAuthorityFinishTheActiveRunBeforeChangingItsGameVersion
        => Get(nameof(SignedIronmonAuthorityFinishTheActiveRunBeforeChangingItsGameVersion));

    /// <summary>
    /// Gets the localized text: Repair cannot change the game or use a different release baseline.
    /// </summary>
    public static string SignedIronmonAuthorityRepairCannotChangeTheGameOrUseADifferent
        => Get(nameof(SignedIronmonAuthorityRepairCannotChangeTheGameOrUseADifferent));

    /// <summary>
    /// Gets the localized text: The installed game does not match a supported signed inventory; a combined game update is required.
    /// </summary>
    public static string SignedIronmonAuthorityTheInstalledGameDoesNotMatchASupportedSigned
        => Get(nameof(SignedIronmonAuthorityTheInstalledGameDoesNotMatchASupportedSigned));

    /// <summary>
    /// Gets the localized text: The installed tracker, scripts or data do not match the signed release.
    /// </summary>
    public static string SignedIronmonAuthorityTheInstalledTrackerScriptsOrDataDoNotMatch
        => Get(nameof(SignedIronmonAuthorityTheInstalledTrackerScriptsOrDataDoNotMatch));

    /// <summary>
    /// Gets the localized text: The previous signed release is not a compatible forward-update baseline.
    /// </summary>
    public static string SignedIronmonAuthorityThePreviousSignedReleaseIsNotACompatibleForward
        => Get(nameof(SignedIronmonAuthorityThePreviousSignedReleaseIsNotACompatibleForward));

    /// <summary>
    /// Gets the localized text: The recovery evidence contains duplicate inventory documents.
    /// </summary>
    public static string SignedIronmonAuthorityTheRecoveryEvidenceContainsDuplicateInventoryDocuments
        => Get(nameof(SignedIronmonAuthorityTheRecoveryEvidenceContainsDuplicateInventoryDocuments));

    /// <summary>
    /// Gets the localized text: The recovery evidence is missing an authenticated inventory.
    /// </summary>
    public static string SignedIronmonAuthorityTheRecoveryEvidenceIsMissingAnAuthenticatedInventory
        => Get(nameof(SignedIronmonAuthorityTheRecoveryEvidenceIsMissingAnAuthenticatedInventory));

    /// <summary>
    /// Gets the localized text: There is no signed inventory for this installed legacy package.
    /// </summary>
    public static string SignedIronmonAuthorityThereIsNoSignedInventoryForThisInstalledLegacy
        => Get(nameof(SignedIronmonAuthorityThereIsNoSignedInventoryForThisInstalledLegacy));

    /// <summary>
    /// Gets the localized text: The release authorization schema is unsupported.
    /// </summary>
    public static string SignedIronmonAuthorityTheReleaseAuthorizationSchemaIsUnsupported
        => Get(nameof(SignedIronmonAuthorityTheReleaseAuthorizationSchemaIsUnsupported));

    /// <summary>
    /// Gets the localized text: The release must describe both distinct tracker package flavors.
    /// </summary>
    public static string SignedIronmonAuthorityTheReleaseMustDescribeBothDistinctTrackerPackageFlavors
        => Get(nameof(SignedIronmonAuthorityTheReleaseMustDescribeBothDistinctTrackerPackageFlavors));

    /// <summary>
    /// Gets the localized text: The transaction bytes do not match their interpreted description.
    /// </summary>
    public static string SignedIronmonAuthorityTheTransactionBytesDoNotMatchTheirInterpretedDescription
        => Get(nameof(SignedIronmonAuthorityTheTransactionBytesDoNotMatchTheirInterpretedDescription));

    /// <summary>
    /// Gets the localized text: The transaction claims files or approvals outside its authenticated release evidence.
    /// </summary>
    public static string SignedIronmonAuthorityTheTransactionClaimsFilesOrApprovalsOutsideItsAuthenticated
        => Get(nameof(SignedIronmonAuthorityTheTransactionClaimsFilesOrApprovalsOutsideItsAuthenticated));

    /// <summary>
    /// Gets the localized text: This authorization cannot relocate the installation or modify game Git state.
    /// </summary>
    public static string SignedIronmonAuthorityThisAuthorizationCannotRelocateTheInstallationOrModifyGame
        => Get(nameof(SignedIronmonAuthorityThisAuthorizationCannotRelocateTheInstallationOrModifyGame));

    /// <summary>
    /// Gets the localized text: The saved tracker navigation does not match this update.
    /// </summary>
    public static string TrackerRelaunchTheSavedTrackerNavigationDoesNotMatchThisUpdate
        => Get(nameof(TrackerRelaunchTheSavedTrackerNavigationDoesNotMatchThisUpdate));

    /// <summary>
    /// Gets the localized text: The tracker navigation context is too large.
    /// </summary>
    public static string TrackerRelaunchTheTrackerNavigationContextIsTooLarge
        => Get(nameof(TrackerRelaunchTheTrackerNavigationContextIsTooLarge));

    /// <summary>
    /// Gets the localized text: The update finished, but the tracker could not start. Open it from your usual shortcut.
    /// </summary>
    public static string TrackerRelaunchTheUpdateFinishedButTheTrackerCouldNotStart
        => Get(nameof(TrackerRelaunchTheUpdateFinishedButTheTrackerCouldNotStart));

    /// <summary>
    /// Gets the localized text: The update finished. Open the tracker normally from your shortcut to continue without administrator privileges.
    /// </summary>
    public static string TrackerRelaunchTheUpdateFinishedOpenTheTrackerNormallyFromYour
        => Get(nameof(TrackerRelaunchTheUpdateFinishedOpenTheTrackerNormallyFromYour));

    /// <summary>
    /// Gets the localized text: A tracker framework name is missing.
    /// </summary>
    public static string TrackerRuntimeCompatibilityATrackerFrameworkNameIsMissing
        => Get(nameof(TrackerRuntimeCompatibilityATrackerFrameworkNameIsMissing));

    /// <summary>
    /// Gets the localized text: The runtime-required package has no unambiguous framework configuration.
    /// </summary>
    public static string TrackerRuntimeCompatibilityTheRuntimeRequiredPackageHasNoUnambiguousFrameworkConfiguration
        => Get(nameof(TrackerRuntimeCompatibilityTheRuntimeRequiredPackageHasNoUnambiguousFrameworkConfiguration));

    /// <summary>
    /// Gets the localized text: The self-contained tracker package does not contain its declared runtime.
    /// </summary>
    public static string TrackerRuntimeCompatibilityTheSelfContainedTrackerPackageDoesNotContainIts
        => Get(nameof(TrackerRuntimeCompatibilityTheSelfContainedTrackerPackageDoesNotContainIts));

    /// <summary>
    /// Gets the localized text: The tracker declares an unsupported framework set.
    /// </summary>
    public static string TrackerRuntimeCompatibilityTheTrackerDeclaresAnUnsupportedFrameworkSet
        => Get(nameof(TrackerRuntimeCompatibilityTheTrackerDeclaresAnUnsupportedFrameworkSet));

    /// <summary>
    /// Gets the localized text: This tracker requires {0} {1} for Windows x64. Install that runtime or choose the self-contained package before updating.
    /// </summary>
    /// <param name="value0">The value for resource placeholder 0.</param>
    /// <param name="value1">The value for resource placeholder 1.</param>
    /// <returns>The localized message with culture-formatted values.</returns>
    public static string TrackerRuntimeCompatibilityThisTrackerRequiresForWindowsX64InstallThatRuntime(object? value0, object? value1)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(TrackerRuntimeCompatibilityThisTrackerRequiresForWindowsX64InstallThatRuntime)), value0, value1);

    /// <summary>
    /// Gets the localized text: This tracker runtime policy requires a newer verified installer.
    /// </summary>
    public static string TrackerRuntimeCompatibilityThisTrackerRuntimePolicyRequiresANewerVerifiedInstaller
        => Get(nameof(TrackerRuntimeCompatibilityThisTrackerRuntimePolicyRequiresANewerVerifiedInstaller));

    /// <summary>
    /// Gets the localized text: Finish the active run before updating the game. Connect the tracker to the game so it can check the run first.
    /// </summary>
    public static string TrackerUpdatePreparationFinishTheActiveRunBeforeUpdatingTheGameConnect
        => Get(nameof(TrackerUpdatePreparationFinishTheActiveRunBeforeUpdatingTheGameConnect));

    /// <summary>
    /// Gets the localized text: The installed release record does not match this tracker. Restore the matching tracker package before updating.
    /// </summary>
    public static string TrackerUpdatePreparationTheInstalledReleaseRecordDoesNotMatchThisTracker
        => Get(nameof(TrackerUpdatePreparationTheInstalledReleaseRecordDoesNotMatchThisTracker));

    /// <summary>
    /// Gets the localized text: The installed release&apos;s verification record could not be found. Use the release&apos;s installer to repair the installation.
    /// </summary>
    public static string TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordCouldNotBe
        => Get(nameof(TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordCouldNotBe));

    /// <summary>
    /// Gets the localized text: The installed release&apos;s verification record names another version.
    /// </summary>
    public static string TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordNamesAnotherVersion
        => Get(nameof(TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordNamesAnotherVersion));

    /// <summary>
    /// Gets the localized text: The installed release&apos;s verification records are missing. Use the release&apos;s installer to repair the installation.
    /// </summary>
    public static string TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordsAreMissingUse
        => Get(nameof(TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordsAreMissingUse));

    /// <summary>
    /// Gets the localized text: The release notes exceed the supported size.
    /// </summary>
    public static string TrackerUpdatePreparationTheReleaseNotesExceedTheSupportedSize
        => Get(nameof(TrackerUpdatePreparationTheReleaseNotesExceedTheSupportedSize));

    /// <summary>
    /// Gets the localized text: A run started while the update was being prepared. Finish the run before updating the game.
    /// </summary>
    public static string TrackerUpdateSessionARunStartedWhileTheUpdateWasBeingPrepared
        => Get(nameof(TrackerUpdateSessionARunStartedWhileTheUpdateWasBeingPrepared));

    /// <summary>
    /// Gets the localized text: The installation changed during review. Check the update again before continuing.
    /// </summary>
    public static string TrackerUpdateSessionTheInstallationChangedDuringReviewCheckTheUpdateAgain
        => Get(nameof(TrackerUpdateSessionTheInstallationChangedDuringReviewCheckTheUpdateAgain));

    /// <summary>
    /// Gets the localized text: A staged or backup file failed content verification.
    /// </summary>
    public static string TransactionStorageAStagedOrBackupFileFailedContentVerification
        => Get(nameof(TransactionStorageAStagedOrBackupFileFailedContentVerification));

    /// <summary>
    /// Gets the localized text: A staged or backup file has an unexpected size.
    /// </summary>
    public static string TransactionStorageAStagedOrBackupFileHasAnUnexpectedSize
        => Get(nameof(TransactionStorageAStagedOrBackupFileHasAnUnexpectedSize));

    /// <summary>
    /// Gets the localized text: Duplicate recovery document properties are not supported.
    /// </summary>
    public static string TransactionStorageDuplicateRecoveryDocumentPropertiesAreNotSupported
        => Get(nameof(TransactionStorageDuplicateRecoveryDocumentPropertiesAreNotSupported));

    /// <summary>
    /// Gets the localized text: Git metadata changed after preparation.
    /// </summary>
    public static string TransactionStorageGitMetadataChangedAfterPreparation
        => Get(nameof(TransactionStorageGitMetadataChangedAfterPreparation));

    /// <summary>
    /// Gets the localized text: The copied Git metadata failed verification.
    /// </summary>
    public static string TransactionStorageTheCopiedGitMetadataFailedVerification
        => Get(nameof(TransactionStorageTheCopiedGitMetadataFailedVerification));

    /// <summary>
    /// Gets the localized text: The metadata tree is too large.
    /// </summary>
    public static string TransactionStorageTheMetadataTreeIsTooLarge
        => Get(nameof(TransactionStorageTheMetadataTreeIsTooLarge));

    /// <summary>
    /// Gets the localized text: The recovery document exceeds its size limit.
    /// </summary>
    public static string TransactionStorageTheRecoveryDocumentExceedsItsSizeLimit
        => Get(nameof(TransactionStorageTheRecoveryDocumentExceedsItsSizeLimit));

    /// <summary>
    /// Gets the localized text: The recovery document is empty.
    /// </summary>
    public static string TransactionStorageTheRecoveryDocumentIsEmpty
        => Get(nameof(TransactionStorageTheRecoveryDocumentIsEmpty));

    /// <summary>
    /// Gets the localized text: The transaction journal is damaged; backups have been retained.
    /// </summary>
    public static string TransactionStorageTheTransactionJournalIsDamagedBackupsHaveBeenRetained
        => Get(nameof(TransactionStorageTheTransactionJournalIsDamagedBackupsHaveBeenRetained));

    /// <summary>
    /// Gets the localized text: The file inventory contains an invalid fingerprint.
    /// </summary>
    public static string UpdateFilePathTheFileInventoryContainsAnInvalidFingerprint
        => Get(nameof(UpdateFilePathTheFileInventoryContainsAnInvalidFingerprint));

    /// <summary>
    /// Gets the localized text: The file inventory contains an unsafe Windows path.
    /// </summary>
    public static string UpdateFilePathTheFileInventoryContainsAnUnsafeWindowsPath
        => Get(nameof(UpdateFilePathTheFileInventoryContainsAnUnsafeWindowsPath));

    /// <summary>
    /// Gets the localized text: Close the Infinite Fusion launcher and installer before updating Ironmon.
    /// </summary>
    public static string UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating
        => Get(nameof(UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating));

    /// <summary>
    /// Gets the localized text: The installation&apos;s game or tracker is running. Close it to continue.
    /// </summary>
    public static string UpdateProcessIdentityTheInstallationSGameOrTrackerIsRunningClose
        => Get(nameof(UpdateProcessIdentityTheInstallationSGameOrTrackerIsRunningClose));

    /// <summary>
    /// Gets the localized text: The process executable could not be inspected.
    /// </summary>
    public static string UpdateProcessIdentityTheProcessExecutableCouldNotBeInspected
        => Get(nameof(UpdateProcessIdentityTheProcessExecutableCouldNotBeInspected));

    /// <summary>
    /// Gets the localized text: The process exited during identity inspection.
    /// </summary>
    public static string UpdateProcessIdentityTheProcessExitedDuringIdentityInspection
        => Get(nameof(UpdateProcessIdentityTheProcessExitedDuringIdentityInspection));

    /// <summary>
    /// Gets the localized text: The process path does not match the shutdown handoff.
    /// </summary>
    public static string UpdateProcessIdentityTheProcessPathDoesNotMatchTheShutdownHandoff
        => Get(nameof(UpdateProcessIdentityTheProcessPathDoesNotMatchTheShutdownHandoff));

    /// <summary>
    /// Gets the localized text: Waiting for the game or tracker to close. Save your progress and close it to continue.
    /// </summary>
    public static string UpdateProcessIdentityWaitingForTheGameOrTrackerToCloseSave
        => Get(nameof(UpdateProcessIdentityWaitingForTheGameOrTrackerToCloseSave));

    /// <summary>
    /// Gets the localized text: An unexpected process connected to the updater handoff.
    /// </summary>
    public static string UpdaterHandoffAnUnexpectedProcessConnectedToTheUpdaterHandoff
        => Get(nameof(UpdaterHandoffAnUnexpectedProcessConnectedToTheUpdaterHandoff));

    /// <summary>
    /// Gets the localized text: Only the tracker process named in the handoff can initiate its shutdown.
    /// </summary>
    public static string UpdaterHandoffOnlyTheTrackerProcessNamedInTheHandoffCan
        => Get(nameof(UpdaterHandoffOnlyTheTrackerProcessNamedInTheHandoffCan));

    /// <summary>
    /// Gets the localized text: The acknowledged updater already exited.
    /// </summary>
    public static string UpdaterHandoffTheAcknowledgedUpdaterAlreadyExited
        => Get(nameof(UpdaterHandoffTheAcknowledgedUpdaterAlreadyExited));

    /// <summary>
    /// Gets the localized text: The administrator handoff belongs to another transaction.
    /// </summary>
    public static string UpdaterHandoffTheAdministratorHandoffBelongsToAnotherTransaction
        => Get(nameof(UpdaterHandoffTheAdministratorHandoffBelongsToAnotherTransaction));

    /// <summary>
    /// Gets the localized text: The game shutdown target is outside this installation.
    /// </summary>
    public static string UpdaterHandoffTheGameShutdownTargetIsOutsideThisInstallation
        => Get(nameof(UpdaterHandoffTheGameShutdownTargetIsOutsideThisInstallation));

    /// <summary>
    /// Gets the localized text: The handoff tracker does not own the connected pipe.
    /// </summary>
    public static string UpdaterHandoffTheHandoffTrackerDoesNotOwnTheConnectedPipe
        => Get(nameof(UpdaterHandoffTheHandoffTrackerDoesNotOwnTheConnectedPipe));

    /// <summary>
    /// Gets the localized text: The independent helper failed authenticated content verification.
    /// </summary>
    public static string UpdaterHandoffTheIndependentHelperFailedAuthenticatedContentVerification
        => Get(nameof(UpdaterHandoffTheIndependentHelperFailedAuthenticatedContentVerification));

    /// <summary>
    /// Gets the localized text: The independent updater could not start.
    /// </summary>
    public static string UpdaterHandoffTheIndependentUpdaterCouldNotStart
        => Get(nameof(UpdaterHandoffTheIndependentUpdaterCouldNotStart));

    /// <summary>
    /// Gets the localized text: The tracker exited before the helper acknowledged readiness.
    /// </summary>
    public static string UpdaterHandoffTheTrackerExitedBeforeTheHelperAcknowledgedReadiness
        => Get(nameof(UpdaterHandoffTheTrackerExitedBeforeTheHelperAcknowledgedReadiness));

    /// <summary>
    /// Gets the localized text: The tracker handoff context is invalid or too large.
    /// </summary>
    public static string UpdaterHandoffTheTrackerHandoffContextIsInvalidOrTooLarge
        => Get(nameof(UpdaterHandoffTheTrackerHandoffContextIsInvalidOrTooLarge));

    /// <summary>
    /// Gets the localized text: The updater handoff is too large.
    /// </summary>
    public static string UpdaterHandoffTheUpdaterHandoffIsTooLarge
        => Get(nameof(UpdaterHandoffTheUpdaterHandoffIsTooLarge));

    /// <summary>
    /// Gets the localized text: The updater handoff length is invalid.
    /// </summary>
    public static string UpdaterHandoffTheUpdaterHandoffLengthIsInvalid
        => Get(nameof(UpdaterHandoffTheUpdaterHandoffLengthIsInvalid));

    /// <summary>
    /// Gets the localized text: The updater handoff pipe name is invalid.
    /// </summary>
    public static string UpdaterHandoffTheUpdaterHandoffPipeNameIsInvalid
        => Get(nameof(UpdaterHandoffTheUpdaterHandoffPipeNameIsInvalid));

    /// <summary>
    /// Gets the localized text: The updater handoff signal is invalid.
    /// </summary>
    public static string UpdaterHandoffTheUpdaterHandoffSignalIsInvalid
        => Get(nameof(UpdaterHandoffTheUpdaterHandoffSignalIsInvalid));

    /// <summary>
    /// Gets the localized text: Embedded updater trust is missing.
    /// </summary>
    public static string UpdaterTrustEmbeddedUpdaterTrustIsMissing
        => Get(nameof(UpdaterTrustEmbeddedUpdaterTrustIsMissing));

    /// <summary>
    /// Gets the localized text: Updater trust contains invalid key identifiers.
    /// </summary>
    public static string UpdaterTrustUpdaterTrustContainsInvalidKeyIdentifiers
        => Get(nameof(UpdaterTrustUpdaterTrustContainsInvalidKeyIdentifiers));

    /// <summary>
    /// Gets the localized text: Cancel
    /// </summary>
    public static string UpdaterWindowCancel
        => Get(nameof(UpdaterWindowCancel));

    /// <summary>
    /// Gets the localized text: Cancellation needs attention. Your recovery copies have been kept. Open the tracker from your usual shortcut.
    /// </summary>
    public static string UpdaterWindowCancellationNeedsAttentionYourRecoveryCopiesHaveBeenKept
        => Get(nameof(UpdaterWindowCancellationNeedsAttentionYourRecoveryCopiesHaveBeenKept));

    /// <summary>
    /// Gets the localized text: Cancelling safely. Please wait while any changed files are restored…
    /// </summary>
    public static string UpdaterWindowCancellingSafelyPleaseWaitWhileAnyChangedFilesAre
        => Get(nameof(UpdaterWindowCancellingSafelyPleaseWaitWhileAnyChangedFilesAre));

    /// <summary>
    /// Gets the localized text: Checking the interrupted update and its recovery backup…
    /// </summary>
    public static string UpdaterWindowCheckingTheInterruptedUpdateAndItsRecoveryBackup
        => Get(nameof(UpdaterWindowCheckingTheInterruptedUpdateAndItsRecoveryBackup));

    /// <summary>
    /// Gets the localized text: Close
    /// </summary>
    public static string UpdaterWindowClose
        => Get(nameof(UpdaterWindowClose));

    /// <summary>
    /// Gets the localized text: Closing the tracker and waiting for the game to finish saving…
    /// </summary>
    public static string UpdaterWindowClosingTheTrackerAndWaitingForTheGameTo
        => Get(nameof(UpdaterWindowClosingTheTrackerAndWaitingForTheGameTo));

    /// <summary>
    /// Gets the localized text: Installing the update and verifying files…
    /// </summary>
    public static string UpdaterWindowInstallingTheUpdateAndVerifyingFiles
        => Get(nameof(UpdaterWindowInstallingTheUpdateAndVerifyingFiles));

    /// <summary>
    /// Gets the localized text: Ironmon update
    /// </summary>
    public static string UpdaterWindowIronmonUpdate
        => Get(nameof(UpdaterWindowIronmonUpdate));

    /// <summary>
    /// Gets the localized text: Preparing independent recovery…
    /// </summary>
    public static string UpdaterWindowPreparingIndependentRecovery
        => Get(nameof(UpdaterWindowPreparingIndependentRecovery));

    /// <summary>
    /// Gets the localized text: Recovery needs attention. Your backups have been kept.
    /// </summary>
    public static string UpdaterWindowRecoveryNeedsAttentionYourBackupsHaveBeenKept
        => Get(nameof(UpdaterWindowRecoveryNeedsAttentionYourBackupsHaveBeenKept));

    /// <summary>
    /// Gets the localized text: Recovery was cancelled before restoration. Your backups have been kept. Reopen the tracker to resume recovery.
    /// </summary>
    public static string UpdaterWindowRecoveryWasCancelledBeforeRestorationYourBackupsHaveBeen
        => Get(nameof(UpdaterWindowRecoveryWasCancelledBeforeRestorationYourBackupsHaveBeen));

    /// <summary>
    /// Gets the localized text: Restoring the previous installation…
    /// </summary>
    public static string UpdaterWindowRestoringThePreviousInstallation
        => Get(nameof(UpdaterWindowRestoringThePreviousInstallation));

    /// <summary>
    /// Gets the localized text: Retry recovery
    /// </summary>
    public static string UpdaterWindowRetryRecovery
        => Get(nameof(UpdaterWindowRetryRecovery));

    /// <summary>
    /// Gets the localized text: Start this helper from the tracker, or use the recovery command for the affected installation and transaction.
    /// </summary>
    public static string UpdaterWindowStartThisHelperFromTheTrackerOrUseThe
        => Get(nameof(UpdaterWindowStartThisHelperFromTheTrackerOrUseThe));

    /// <summary>
    /// Gets the localized text: The previous installation has been restored.
    /// </summary>
    public static string UpdaterWindowThePreviousInstallationHasBeenRestored
        => Get(nameof(UpdaterWindowThePreviousInstallationHasBeenRestored));

    /// <summary>
    /// Gets the localized text: The update finished successfully.
    /// </summary>
    public static string UpdaterWindowTheUpdateFinishedSuccessfully
        => Get(nameof(UpdaterWindowTheUpdateFinishedSuccessfully));

    /// <summary>
    /// Gets the localized text: The update was cancelled. Your installed game and tracker have not been replaced.
    /// </summary>
    public static string UpdaterWindowTheUpdateWasCancelledYourInstalledGameAndTracker
        => Get(nameof(UpdaterWindowTheUpdateWasCancelledYourInstalledGameAndTracker));

    /// <summary>
    /// Gets the localized text: A destination changed immediately before its planned mutation.
    /// </summary>
    public static string UpdateTransactionADestinationChangedImmediatelyBeforeItsPlannedMutation
        => Get(nameof(UpdateTransactionADestinationChangedImmediatelyBeforeItsPlannedMutation));

    /// <summary>
    /// Gets the localized text: A managed file changed outside the transaction; its backup has been retained.
    /// </summary>
    public static string UpdateTransactionAManagedFileChangedOutsideTheTransactionItsBackup
        => Get(nameof(UpdateTransactionAManagedFileChangedOutsideTheTransactionItsBackup));

    /// <summary>
    /// Gets the localized text: A managed file is unexpectedly a directory during recovery.
    /// </summary>
    public static string UpdateTransactionAManagedFileIsUnexpectedlyADirectoryDuringRecovery
        => Get(nameof(UpdateTransactionAManagedFileIsUnexpectedlyADirectoryDuringRecovery));

    /// <summary>
    /// Gets the localized text: A new directory was replaced with an unexpected file during recovery.
    /// </summary>
    public static string UpdateTransactionANewDirectoryWasReplacedWithAnUnexpectedFile
        => Get(nameof(UpdateTransactionANewDirectoryWasReplacedWithAnUnexpectedFile));

    /// <summary>
    /// Gets the localized text: A planned new directory is unexpectedly occupied.
    /// </summary>
    public static string UpdateTransactionAPlannedNewDirectoryIsUnexpectedlyOccupied
        => Get(nameof(UpdateTransactionAPlannedNewDirectoryIsUnexpectedlyOccupied));

    /// <summary>
    /// Gets the localized text: A recovery backup failed verification.
    /// </summary>
    public static string UpdateTransactionARecoveryBackupFailedVerification
        => Get(nameof(UpdateTransactionARecoveryBackupFailedVerification));

    /// <summary>
    /// Gets the localized text: A removed directory is unexpectedly occupied during recovery.
    /// </summary>
    public static string UpdateTransactionARemovedDirectoryIsUnexpectedlyOccupiedDuringRecovery
        => Get(nameof(UpdateTransactionARemovedDirectoryIsUnexpectedlyOccupiedDuringRecovery));

    /// <summary>
    /// Gets the localized text: A replacement payload failed verification.
    /// </summary>
    public static string UpdateTransactionAReplacementPayloadFailedVerification
        => Get(nameof(UpdateTransactionAReplacementPayloadFailedVerification));

    /// <summary>
    /// Gets the localized text: Git metadata changed outside the transaction; recovery requires attention.
    /// </summary>
    public static string UpdateTransactionGitMetadataChangedOutsideTheTransactionRecoveryRequiresAttention
        => Get(nameof(UpdateTransactionGitMetadataChangedOutsideTheTransactionRecoveryRequiresAttention));

    /// <summary>
    /// Gets the localized text: Git metadata is unexpectedly a file.
    /// </summary>
    public static string UpdateTransactionGitMetadataIsUnexpectedlyAFile
        => Get(nameof(UpdateTransactionGitMetadataIsUnexpectedlyAFile));

    /// <summary>
    /// Gets the localized text: Git metadata no longer matches the reviewed state.
    /// </summary>
    public static string UpdateTransactionGitMetadataNoLongerMatchesTheReviewedState
        => Get(nameof(UpdateTransactionGitMetadataNoLongerMatchesTheReviewedState));

    /// <summary>
    /// Gets the localized text: Installation files changed since review; application or recovery cannot safely continue.
    /// </summary>
    public static string UpdateTransactionInstallationFilesChangedSinceReviewApplicationOrRecoveryCannot
        => Get(nameof(UpdateTransactionInstallationFilesChangedSinceReviewApplicationOrRecoveryCannot));

    /// <summary>
    /// Gets the localized text: The active transaction does not match its descriptor.
    /// </summary>
    public static string UpdateTransactionTheActiveTransactionDoesNotMatchItsDescriptor
        => Get(nameof(UpdateTransactionTheActiveTransactionDoesNotMatchItsDescriptor));

    /// <summary>
    /// Gets the localized text: The final installation does not match the complete reviewed result.
    /// </summary>
    public static string UpdateTransactionTheFinalInstallationDoesNotMatchTheCompleteReviewed
        => Get(nameof(UpdateTransactionTheFinalInstallationDoesNotMatchTheCompleteReviewed));

    /// <summary>
    /// Gets the localized text: The Git rollback backup failed verification.
    /// </summary>
    public static string UpdateTransactionTheGitRollbackBackupFailedVerification
        => Get(nameof(UpdateTransactionTheGitRollbackBackupFailedVerification));

    /// <summary>
    /// Gets the localized text: The installation identity changed after review.
    /// </summary>
    public static string UpdateTransactionTheInstallationIdentityChangedAfterReview
        => Get(nameof(UpdateTransactionTheInstallationIdentityChangedAfterReview));

    /// <summary>
    /// Gets the localized text: The pending update record is invalid.
    /// </summary>
    public static string UpdateTransactionThePendingUpdateRecordIsInvalid
        => Get(nameof(UpdateTransactionThePendingUpdateRecordIsInvalid));

    /// <summary>
    /// Gets the localized text: The pending update record is too large.
    /// </summary>
    public static string UpdateTransactionThePendingUpdateRecordIsTooLarge
        => Get(nameof(UpdateTransactionThePendingUpdateRecordIsTooLarge));

    /// <summary>
    /// Gets the localized text: The prepared Git tree failed verification.
    /// </summary>
    public static string UpdateTransactionThePreparedGitTreeFailedVerification
        => Get(nameof(UpdateTransactionThePreparedGitTreeFailedVerification));

    /// <summary>
    /// Gets the localized text: There is insufficient free space to update and retain a complete recovery backup.
    /// </summary>
    public static string UpdateTransactionThereIsInsufficientFreeSpaceToUpdateAndRetain
        => Get(nameof(UpdateTransactionThereIsInsufficientFreeSpaceToUpdateAndRetain));

    /// <summary>
    /// Gets the localized text: The saved installation identity does not match this directory.
    /// </summary>
    public static string UpdateTransactionTheSavedInstallationIdentityDoesNotMatchThisDirectory
        => Get(nameof(UpdateTransactionTheSavedInstallationIdentityDoesNotMatchThisDirectory));

    /// <summary>
    /// Gets the localized text: The transaction directory identity does not match its descriptor.
    /// </summary>
    public static string UpdateTransactionTheTransactionDirectoryIdentityDoesNotMatchItsDescriptor
        => Get(nameof(UpdateTransactionTheTransactionDirectoryIdentityDoesNotMatchItsDescriptor));

    /// <summary>
    /// Gets the localized text: The transaction journal has unsupported or inconsistent progress.
    /// </summary>
    public static string UpdateTransactionTheTransactionJournalHasUnsupportedOrInconsistentProgress
        => Get(nameof(UpdateTransactionTheTransactionJournalHasUnsupportedOrInconsistentProgress));

    /// <summary>
    /// Gets the localized text: The unfinished transaction requires recovery before another update can start.
    /// </summary>
    public static string UpdateTransactionTheUnfinishedTransactionRequiresRecoveryBeforeAnotherUpdateCan
        => Get(nameof(UpdateTransactionTheUnfinishedTransactionRequiresRecoveryBeforeAnotherUpdateCan));

    /// <summary>
    /// Gets the localized text: This recovery engine, installation path or directory identity does not match the transaction.
    /// </summary>
    public static string UpdateTransactionThisRecoveryEngineInstallationPathOrDirectoryIdentityDoes
        => Get(nameof(UpdateTransactionThisRecoveryEngineInstallationPathOrDirectoryIdentityDoes));

    /// <summary>
    /// Gets the localized text: This transaction identifier already has recovery data.
    /// </summary>
    public static string UpdateTransactionThisTransactionIdentifierAlreadyHasRecoveryData
        => Get(nameof(UpdateTransactionThisTransactionIdentifierAlreadyHasRecoveryData));

    /// <summary>
    /// Gets the localized text: This transaction must use recovery rather than replaying application.
    /// </summary>
    public static string UpdateTransactionThisTransactionMustUseRecoveryRatherThanReplayingApplication
        => Get(nameof(UpdateTransactionThisTransactionMustUseRecoveryRatherThanReplayingApplication));

    /// <summary>
    /// Gets the localized text: This update has already started replacing files and requires independent recovery.
    /// </summary>
    public static string UpdateTransactionThisUpdateHasAlreadyStartedReplacingFilesAndRequires
        => Get(nameof(UpdateTransactionThisUpdateHasAlreadyStartedReplacingFilesAndRequires));

    /// <summary>
    /// Gets the localized text: A different Ironmon Tracker shortcut already exists on the desktop. It was left unchanged.
    /// </summary>
    public static string WindowsSetupPlatformADifferentIronmonTrackerShortcutAlreadyExistsOnThe
        => Get(nameof(WindowsSetupPlatformADifferentIronmonTrackerShortcutAlreadyExistsOnThe));

    /// <summary>
    /// Gets the localized text: Finish recovery and install Microsoft WebView2 before opening the tracker.
    /// </summary>
    public static string WindowsSetupPlatformFinishRecoveryAndInstallMicrosoftWebView2BeforeOpeningThe
        => Get(nameof(WindowsSetupPlatformFinishRecoveryAndInstallMicrosoftWebView2BeforeOpeningThe));

    /// <summary>
    /// Gets the localized text: Ironmon Setup requires Windows 10 version 1809 or newer on x64 Windows.
    /// </summary>
    public static string WindowsSetupPlatformIronmonSetupRequiresWindows10Version1809OrNewer
        => Get(nameof(WindowsSetupPlatformIronmonSetupRequiresWindows10Version1809OrNewer));

    /// <summary>
    /// Gets the localized text: Microsoft&apos;s download redirect is incomplete.
    /// </summary>
    public static string WindowsSetupPlatformMicrosoftSDownloadRedirectIsIncomplete
        => Get(nameof(WindowsSetupPlatformMicrosoftSDownloadRedirectIsIncomplete));

    /// <summary>
    /// Gets the localized text: Microsoft WebView2 could not be detected after installation. Complete any Windows prompt and retry.
    /// </summary>
    public static string WindowsSetupPlatformMicrosoftWebView2CouldNotBeDetectedAfterInstallationComplete
        => Get(nameof(WindowsSetupPlatformMicrosoftWebView2CouldNotBeDetectedAfterInstallationComplete));

    /// <summary>
    /// Gets the localized text: Open the Ironmon tracker
    /// </summary>
    public static string WindowsSetupPlatformOpenTheIronmonTracker
        => Get(nameof(WindowsSetupPlatformOpenTheIronmonTracker));

    /// <summary>
    /// Gets the localized text: The installed tracker could not be found for its shortcut.
    /// </summary>
    public static string WindowsSetupPlatformTheInstalledTrackerCouldNotBeFoundForIts
        => Get(nameof(WindowsSetupPlatformTheInstalledTrackerCouldNotBeFoundForIts));

    /// <summary>
    /// Gets the localized text: The installed tracker could not be opened.
    /// </summary>
    public static string WindowsSetupPlatformTheInstalledTrackerCouldNotBeOpened
        => Get(nameof(WindowsSetupPlatformTheInstalledTrackerCouldNotBeOpened));

    /// <summary>
    /// Gets the localized text: The prerequisite bootstrapper exceeds the supported size.
    /// </summary>
    public static string WindowsSetupPlatformThePrerequisiteBootstrapperExceedsTheSupportedSize
        => Get(nameof(WindowsSetupPlatformThePrerequisiteBootstrapperExceedsTheSupportedSize));

    /// <summary>
    /// Gets the localized text: The prerequisite did not have a valid Microsoft Corporation signature. It was not run.
    /// </summary>
    public static string WindowsSetupPlatformThePrerequisiteDidNotHaveAValidMicrosoftCorporation
        => Get(nameof(WindowsSetupPlatformThePrerequisiteDidNotHaveAValidMicrosoftCorporation));

    /// <summary>
    /// Gets the localized text: The prerequisite download did not remain on Microsoft&apos;s secure download service.
    /// </summary>
    public static string WindowsSetupPlatformThePrerequisiteDownloadDidNotRemainOnMicrosoftS
        => Get(nameof(WindowsSetupPlatformThePrerequisiteDownloadDidNotRemainOnMicrosoftS));

    /// <summary>
    /// Gets the localized text: Windows could not start the Microsoft prerequisite installer.
    /// </summary>
    public static string WindowsSetupPlatformWindowsCouldNotStartTheMicrosoftPrerequisiteInstaller
        => Get(nameof(WindowsSetupPlatformWindowsCouldNotStartTheMicrosoftPrerequisiteInstaller));

    /// <summary>
    /// Gets the localized text: Windows could not verify the prerequisite publisher.
    /// </summary>
    public static string WindowsSetupPlatformWindowsCouldNotVerifyThePrerequisitePublisher
        => Get(nameof(WindowsSetupPlatformWindowsCouldNotVerifyThePrerequisitePublisher));

    /// <summary>
    /// Gets the localized text: Windows shortcut support is unavailable.
    /// </summary>
    public static string WindowsSetupPlatformWindowsShortcutSupportIsUnavailable
        => Get(nameof(WindowsSetupPlatformWindowsShortcutSupportIsUnavailable));

    /// <summary>
    /// Gets the localized text: Administrator permission was declined. The installation was not replaced.
    /// </summary>
    public static string WindowsUpdateAccessAdministratorPermissionWasDeclinedTheInstallationWasNotReplaced
        => Get(nameof(WindowsUpdateAccessAdministratorPermissionWasDeclinedTheInstallationWasNotReplaced));

    /// <summary>
    /// Gets the localized text: Ironmon Tracker
    /// </summary>
    public static string WindowsUpdateAccessIronmonTracker
        => Get(nameof(WindowsUpdateAccessIronmonTracker));

    /// <summary>
    /// Gets the localized text: The installation has no existing parent directory.
    /// </summary>
    public static string WindowsUpdateAccessTheInstallationHasNoExistingParentDirectory
        => Get(nameof(WindowsUpdateAccessTheInstallationHasNoExistingParentDirectory));

    /// <summary>
    /// Gets the localized text: The installation helper needs administrator permission.
    /// </summary>
    public static string WindowsUpdateAccessTheInstallationHelperNeedsAdministratorPermission
        => Get(nameof(WindowsUpdateAccessTheInstallationHelperNeedsAdministratorPermission));

    /// <summary>
    /// Gets the localized text: The installation path could not be held stable.
    /// </summary>
    public static string WindowsUpdateAccessTheInstallationPathCouldNotBeHeldStable
        => Get(nameof(WindowsUpdateAccessTheInstallationPathCouldNotBeHeldStable));

    /// <summary>
    /// Gets the localized text: The protected updater cache allows ordinary-user modification.
    /// </summary>
    public static string WindowsUpdateAccessTheProtectedUpdaterCacheAllowsOrdinaryUserModification
        => Get(nameof(WindowsUpdateAccessTheProtectedUpdaterCacheAllowsOrdinaryUserModification));

    /// <summary>
    /// Gets the localized text: The protected updater cache has an unexpected owner.
    /// </summary>
    public static string WindowsUpdateAccessTheProtectedUpdaterCacheHasAnUnexpectedOwner
        => Get(nameof(WindowsUpdateAccessTheProtectedUpdaterCacheHasAnUnexpectedOwner));

    /// <summary>
    /// Gets the localized text: The updater file cannot be read with the current permissions.
    /// </summary>
    public static string WindowsUpdateAccessTheUpdaterFileCannotBeReadWithTheCurrent
        => Get(nameof(WindowsUpdateAccessTheUpdaterFileCannotBeReadWithTheCurrent));

    /// <summary>
    /// Gets the localized text: The updater file could not be found.
    /// </summary>
    public static string WindowsUpdateAccessTheUpdaterFileCouldNotBeFound
        => Get(nameof(WindowsUpdateAccessTheUpdaterFileCouldNotBeFound));

    /// <summary>
    /// Gets the localized text: The updater file could not be opened safely.
    /// </summary>
    public static string WindowsUpdateAccessTheUpdaterFileCouldNotBeOpenedSafely
        => Get(nameof(WindowsUpdateAccessTheUpdaterFileCouldNotBeOpenedSafely));

    /// <summary>
    /// Gets the localized text: Windows did not start the administrator helper.
    /// </summary>
    public static string WindowsUpdateAccessWindowsDidNotStartTheAdministratorHelper
        => Get(nameof(WindowsUpdateAccessWindowsDidNotStartTheAdministratorHelper));

    /// <summary>
    /// Gets the localized text: The ZIP installation changed after preparation; prepare it again before updating.
    /// </summary>
    public static string ZipAdoptionPreparationTheZIPInstallationChangedAfterPreparationPrepareItAgain
        => Get(nameof(ZipAdoptionPreparationTheZIPInstallationChangedAfterPreparationPrepareItAgain));

    /// <summary>
    /// Gets the localized text: Adoption requires exact commit objects, not tags or other object types.
    /// </summary>
    public static string ZipGameAdopterAdoptionRequiresExactCommitObjectsNotTagsOrOther
        => Get(nameof(ZipGameAdopterAdoptionRequiresExactCommitObjectsNotTagsOrOther));

    /// <summary>
    /// Gets the localized text: An exact lowercase Git object ID is required.
    /// </summary>
    public static string ZipGameAdopterAnExactLowercaseGitObjectIDIsRequired
        => Get(nameof(ZipGameAdopterAnExactLowercaseGitObjectIDIsRequired));

    /// <summary>
    /// Gets the localized text: Git could not verify or prepare ZIP adoption.
    /// </summary>
    public static string ZipGameAdopterGitCouldNotVerifyOrPrepareZIPAdoption
        => Get(nameof(ZipGameAdopterGitCouldNotVerifyOrPrepareZIPAdoption));

    /// <summary>
    /// Gets the localized text: The approved game tree contains unsupported entries.
    /// </summary>
    public static string ZipGameAdopterTheApprovedGameTreeContainsUnsupportedEntries
        => Get(nameof(ZipGameAdopterTheApprovedGameTreeContainsUnsupportedEntries));

    /// <summary>
    /// Gets the localized text: The baseline contains an invalid byte fingerprint.
    /// </summary>
    public static string ZipGameAdopterTheBaselineContainsAnInvalidByteFingerprint
        => Get(nameof(ZipGameAdopterTheBaselineContainsAnInvalidByteFingerprint));

    /// <summary>
    /// Gets the localized text: The baseline inventory contains duplicate paths or unsupported file modes.
    /// </summary>
    public static string ZipGameAdopterTheBaselineInventoryContainsDuplicatePathsOrUnsupportedFile
        => Get(nameof(ZipGameAdopterTheBaselineInventoryContainsDuplicatePathsOrUnsupportedFile));

    /// <summary>
    /// Gets the localized text: The baseline inventory has an unsupported file count.
    /// </summary>
    public static string ZipGameAdopterTheBaselineInventoryHasAnUnsupportedFileCount
        => Get(nameof(ZipGameAdopterTheBaselineInventoryHasAnUnsupportedFileCount));

    /// <summary>
    /// Gets the localized text: The baseline inventory must authenticate the executable and game INI.
    /// </summary>
    public static string ZipGameAdopterTheBaselineInventoryMustAuthenticateTheExecutableAndGame
        => Get(nameof(ZipGameAdopterTheBaselineInventoryMustAuthenticateTheExecutableAndGame));

    /// <summary>
    /// Gets the localized text: The game does not match one unambiguous approved ZIP baseline. Modified or mixed installations require a separate supported inventory.
    /// </summary>
    public static string ZipGameAdopterTheGameDoesNotMatchOneUnambiguousApprovedZIP
        => Get(nameof(ZipGameAdopterTheGameDoesNotMatchOneUnambiguousApprovedZIP));

    /// <summary>
    /// Gets the localized text: The game inventory requires an unsupported metadata or attribute policy.
    /// </summary>
    public static string ZipGameAdopterTheGameInventoryRequiresAnUnsupportedMetadataOrAttribute
        => Get(nameof(ZipGameAdopterTheGameInventoryRequiresAnUnsupportedMetadataOrAttribute));

    /// <summary>
    /// Gets the localized text: The game tree has a file that also serves as a directory on Windows.
    /// </summary>
    public static string ZipGameAdopterTheGameTreeHasAFileThatAlsoServes
        => Get(nameof(ZipGameAdopterTheGameTreeHasAFileThatAlsoServes));

    /// <summary>
    /// Gets the localized text: The game tree has paths that collide on Windows.
    /// </summary>
    public static string ZipGameAdopterTheGameTreeHasPathsThatCollideOnWindows
        => Get(nameof(ZipGameAdopterTheGameTreeHasPathsThatCollideOnWindows));

    /// <summary>
    /// Gets the localized text: The historical inventory does not cover the complete approved Git tree.
    /// </summary>
    public static string ZipGameAdopterTheHistoricalInventoryDoesNotCoverTheCompleteApproved
        => Get(nameof(ZipGameAdopterTheHistoricalInventoryDoesNotCoverTheCompleteApproved));

    /// <summary>
    /// Gets the localized text: ZIP adoption requires cache and preparation directories outside the installation.
    /// </summary>
    public static string ZipGameAdopterZIPAdoptionRequiresCacheAndPreparationDirectoriesOutsideThe
        => Get(nameof(ZipGameAdopterZIPAdoptionRequiresCacheAndPreparationDirectoriesOutsideThe));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressPreparingDownload
        => Get(nameof(ProgressPreparingDownload));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressDownloadingGame
        => Get(nameof(ProgressDownloadingGame));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressProcessingGame
        => Get(nameof(ProgressProcessingGame));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressVerifyingGame
        => Get(nameof(ProgressVerifyingGame));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressPreparingGame
        => Get(nameof(ProgressPreparingGame));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressExtractingFiles
        => Get(nameof(ProgressExtractingFiles));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressPreparingRecovery
        => Get(nameof(ProgressPreparingRecovery));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressVerifyingFiles
        => Get(nameof(ProgressVerifyingFiles));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressInstallingFiles
        => Get(nameof(ProgressInstallingFiles));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressRestoringFiles
        => Get(nameof(ProgressRestoringFiles));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressDownloadingSprites
        => Get(nameof(ProgressDownloadingSprites));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    public static string ProgressDownloadingPackage
        => Get(nameof(ProgressDownloadingPackage));

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    /// <param name="completed">The completed units.</param>
    /// <param name="total">The known total units.</param>
    /// <returns>The formatted measurement.</returns>
    public static string ProgressCounts(long completed, long total)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(ProgressCounts)), completed, total);

    /// <summary>
    /// Gets the localized installation progress text.
    /// </summary>
    /// <param name="megabytes">The received binary megabytes.</param>
    /// <returns>The formatted measurement.</returns>
    public static string ProgressReceived(double megabytes)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(ProgressReceived)), megabytes);

    /// <summary>
    /// Gets localized package byte progress.
    /// </summary>
    /// <param name="received">The received binary megabytes.</param>
    /// <param name="total">The total binary megabytes.</param>
    /// <returns>The formatted byte counts.</returns>
    public static string ProgressPackageBytes(double received, double total)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(ProgressPackageBytes)), received, total);

    /// <summary>
    /// Gets elapsed operation time and the age of the last measured progress.
    /// </summary>
    /// <param name="minutes">The elapsed minutes.</param>
    /// <param name="seconds">The seconds since the last measurement.</param>
    /// <returns>The formatted activity timing.</returns>
    public static string ProgressElapsed(double minutes, long seconds)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(ProgressElapsed)), minutes, seconds);

    /// <summary>
    /// Gets measured work when the total has not been determined.
    /// </summary>
    /// <param name="completed">The completed units.</param>
    /// <returns>The formatted count.</returns>
    public static string ProgressProcessed(long completed)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(ProgressProcessed)), completed);

    /// <summary>
    /// Gets the invalid administrator progress response message.
    /// </summary>
    public static string ProgressInvalidResponse
        => Get(nameof(ProgressInvalidResponse));

    /// <summary>
    /// Gets the heading for an installation in progress.
    /// </summary>
    public static string ProgressInstallationInProgress
        => Get(nameof(ProgressInstallationInProgress));
}
