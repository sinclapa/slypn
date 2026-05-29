# Objective

Help me create a website for a Parkinson's support group called South London Younger Parkinson's Network. 

South London Younger Parkinson’s Network (SLYPN) is a support network for younger people, who are working age and live with Parkinson’s in South London.

1 in 20 people diagnosed with Parkinson’s is under 40, the SLYPN is affiliated to Parkinson’s UK and provides a valuable support network to people diagnosed with Parkinson’s at a young age.  Founders, Helen Stoinanov, Kate Wellington & Sarah Webb created it in 2011 to support the South London demographic who could not meet during the day, but wanted to find out more about symptoms, medication, treatments and to find general support.

South London Young Parkinson’s Network members are invited to regular coffee meet-ups, drinks and activities, plus our fundraising events.

The site is for a community to come together.

Inspiration should come from https://www.parkinsons.org.uk/
Content examples on https://slypn.org.uk/

# Features
1. Should have a news letter section, [example](brief/SLYPN_Newsletter_MAY_2026.docx)
2. Blog section
3. Articles section
4. Upcoming events as some form of calendar and list
5. Links to useful resources
6. The site should be public
7. Login section using entra and social media identification
   1. Administrators can manage all site features including managing members
   2. Contributors can create and edit articles that they have written
8. It should be easy for contributors and admins to create new content from a browser
   1. Work in progress is saved as they edit
   2. making available to the main site should require approval
9.  Cookie disclaimer
10. Create powershell scripts for setup, start and stop to run locally
11. Grafana observability including faro for ui and opentel for api
12. Github automated build and deploy with preview

# Technology
Hosted on Azure it should be almost free to host and run and traffic will be light. Prefer C# for API. UI have much more free reign but Vue is favorable.