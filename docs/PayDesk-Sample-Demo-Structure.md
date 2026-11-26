# PayDesk Sample Demo Structure

## Intent

We want to demonstrate the following features, especially in the context of first containers, then Kubernetes.

* uid/gid/filesystem security
* Linux capabilities
* Seccomp/AppArmor

These could be demoed by:

* naive containerization, causing normal functionality to stop working
* naive correction, which leaves security holes
* proper correction, which provides only necessary permissions to the app

Possible failure scenarios:

* Run application with a volume mounted in docker. Upload fails because of write permissions
* Run application with capabilities dropped in docker. Various things fail, in particular ping
* Run application with default seccomp profile applied, and show/discuss impact

Additional scenarios, nice to have:

* Trick the file upload into uploading executable file, possible replacing the consolidator or the main executable
* Show how seccomp can prevent even a malicious uploaded executable from doing much damage
