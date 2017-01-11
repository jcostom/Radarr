﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles
{
    public interface IRenameMovieFileService
    {
        List<RenameMovieFilePreview> GetRenamePreviews(int movieId);
    }

    public class RenameMovieFileService : IRenameMovieFileService,
                                          IExecute<RenameMovieFilesCommand>,
                                          IExecute<RenameMovieCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveMovieFiles _movieFileMover;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly Logger _logger;
    
        public RenameMovieFileService(IMovieService movieService,
                                      IMediaFileService mediaFileService,
                                      IMoveMovieFiles movieFileMover,
                                      IEventAggregator eventAggregator,
                                      IBuildFileNames filenameBuilder,
                                      Logger logger)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _movieFileMover = movieFileMover;
            _eventAggregator = eventAggregator;
            _filenameBuilder = filenameBuilder;
            _logger = logger;
        }

        public List<RenameMovieFilePreview> GetRenamePreviews(int movieId)
        {
            var movie = _movieService.GetMovie(movieId);
            var file = _mediaFileService.GetFilesByMovie(movieId);

            return GetPreviews(movie, file).OrderByDescending(m => m.MovieId).ToList(); //TODO: Would really like to not have these be lists

        }

        private IEnumerable<RenameMovieFilePreview> GetPreviews(Movie movie, List<MovieFile> files)
        {
            foreach(var file in files)
            {
                var movieFilePath = Path.Combine(movie.Path, file.RelativePath);

                var newName = _filenameBuilder.BuildFileName(movie, file);
                var newPath = _filenameBuilder.BuildFilePath(movie, newName, Path.GetExtension(movieFilePath));

                if(!movieFilePath.PathEquals(newPath, StringComparison.Ordinal))
                {
                    yield return new RenameMovieFilePreview
                    {
                        MovieId = movie.Id,
                        MovieFileId = file.Id,
                        ExistingPath = file.RelativePath,
                        NewPath = movie.Path.GetRelativePath(newPath)
                    };
                }

            }
            
        }

        private void RenameFiles(List<MovieFile> movieFiles, Movie movie)
        {
            var renamed = new List<MovieFile>();

            foreach(var movieFile in movieFiles)
            {
                var movieFilePath = Path.Combine(movie.Path, movieFile.RelativePath);

                try
                {
                    _logger.Debug("Renaming movie file: {0}", movieFile);
                    _movieFileMover.MoveMovieFile(movieFile, movie);

                    _mediaFileService.Update(movieFile);
                    renamed.Add(movieFile);

                    _logger.Debug("Renamed movie file: {0}", movieFile);

                }
                catch(SameFilenameException ex)
                {
                    _logger.Debug("File not renamed, source and destination are the same: {0}", ex.Filename);
                }
                catch(Exception ex)
                {
                    _logger.Error(ex, "Failed to rename file: " + movieFilePath);
                }
            }
        }

        public void Execute(RenameMovieFilesCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);
            var movieFiles = _mediaFileService.GetMovies(message.Files);

            _logger.ProgressInfo("Renaming {0} files for {1}", movieFiles.Count, movie.Title);
            RenameFiles(movieFiles, movie);
            _logger.ProgressInfo("Selected movie files renamed for {0}", movie.Title);
        }

        public void Execute(RenameMovieCommand message)
        {
            _logger.Debug("Renaming all files for selected movie");
            var moviesToRename = _movieService.GetMovies(message.MovieIds);

            foreach(var movie in moviesToRename)
            {
                var movieFiles = _mediaFileService.GetFilesByMovie(movie.Id);
                _logger.ProgressInfo("Renaming all files in movie: {0}", movie.Title);
                RenameFiles(movieFiles, movie);
                _logger.ProgressInfo("All movie files renamed for {0}", movie.Title);
            }
            
        }
    }
}