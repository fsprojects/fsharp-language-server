;;; config.el --- fsharp Layer config File for Spacemacs
;;
;; Copyright (c) 2012-2018 Sylvain Benner & Contributors
;;
;; Author: Chris Marchetti <cam.marchetti@gmail.com>
;; URL: https://github.com/syl20bnr/spacemacs
;;
;; This file is not part of GNU Emacs.
;;
;;; License: GPLv3
(spacemacs|define-jump-handlers fsharp-mode)

(defvar fsharp2-backend 'lsp
 "The backend to use for IDE features. Possible values are `fsac' and `lsp'")

(defvar fsharp2-lsp-executable "FSharpLanguageServer"
 "The location of the FSharpLanguageServer executable")
