# Dokploy Aspire Integration

## Description
This was built on the Aspirifridays stream with Maddy Montaquila, David Fowler, and Damian Edwards with support from Mauricio the maintainer of Dokploy.

## The integration itself
The actual classes used are in the apphost project in the dokploy.cs file. They have not been pulled out into a neat and usable package or project by itself or anything.

## What's there and what's missing
It *will*:
This current implementation is able to create a project on your dokploy server and add a registry to it. This registry will be added to the list of linked registries in dokploy.
Then it takes your apphost resources, builds them, and then pushes them to the registry.
After that it will, for each resource, go through and add applications in that same project which use a docker provider to pull from the registry in the same project.
Then it will assign these applications a domain (the port is currently hardcoded to 8080) and afterwards start them up.

It *will not*:
Make these applications connected. Their wiring through the environment variables are simply not set up yet.
Create domains for each resources endpoints.
Allow you to add parameters yourself configuring anything like this. The only parameterized thing is currently the dokploy api key the integration uses to call the dokploy api.
**Important to know:** the registry will be created but it needs a custom domain that you own or have set up in order to wire it through. Its domain will also be hardcoded in the file too.

## Disclaimer:
Not only was this done during a livestream by me, a student, in the driving seat but it was also half using AI and half running purely as fast as I could as a 
windows voice to text for the far more intelligent people than me on the livestream. This is far from what I would call nice or beautiful. But it's freely available for anyone to dive into right here.
